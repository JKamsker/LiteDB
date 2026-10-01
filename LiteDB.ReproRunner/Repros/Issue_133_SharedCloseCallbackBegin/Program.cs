using System.Diagnostics;
using System.Reflection;
using LiteDB;
using LiteDB.Engine;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_133_SharedCloseCallbackBegin;

internal static class Program
{
    private const string Refusal = "Cannot open a transaction handle from inside an operation retaining its shared writer ownership.";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    private static int Main()
    {
        var host = ReproHostClient.CreateDefault();
        ReproConfigurationReporter.SendConfiguration(host);
        var directory = Path.Combine(Path.GetTempPath(), "i133-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Filename-backed Shared opens normalize this physical identity. Custom streams
        // intentionally do not: provide the identical namespace, including Darwin /var.
        var identity = typeof(LiteDatabase).Assembly.GetTypes().Single(type => type.Name == "DatabaseFileIdentity");
        directory = (string)identity.GetMethod("CanonicalPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { directory })!;
        host.SendLog("Original fixtures retained at " + directory);
        typeof(LiteDatabase).Assembly.GetType("LiteDB.LiteDBPragmas")
            ?.GetMethod("I_AM_AWARE_MY_DATABASE_BREAKS_WHEN_I_USE_THIS")?.Invoke(null, null);
        try
        {
            var outcomes = new List<bool>();
            foreach (var encrypted in new[] { false, true })
            {
                Run(directory, encrypted, otherFile: true);
                outcomes.Add(Run(directory, encrypted, otherFile: false));
            }
            if (outcomes.Distinct().Count() != 1) throw new Exception("Mixed plain/encrypted outcomes.");
            var reproduced = outcomes[0];
            var message = reproduced ? "NATIVE_SELF_WAIT_VERIFIED: both close callbacks reached their own native dependency."
                : "FIXED_VERIFIED: callbacks refused before waiting; controls and cold state verified.";
            Console.WriteLine(message);
            host.SendResult(reproduced, message);
            return reproduced ? 0 : 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            host.SendResult(false, "Unexpected reproduction failure.", new { Exception = error.ToString() });
            return 1;
        }
    }

    private static bool Run(string directory, bool encrypted, bool otherFile)
    {
        var password = encrypted ? "secret" : null;
        var name = (encrypted ? "encrypted" : "plain") + (otherFile ? "-other" : "-same");
        var file = Path.Combine(directory, name + ".db");
        var destination = otherFile ? file + ".other" : file;
        Seed(file, password);
        if (otherFile) Seed(destination, password);
        // Warm the normal facade before creating the custom-stream owner. Its inert
        // cached child exposes the exact native waiter in production builds.
        var peerEngine = new SharedEngine(new EngineSettings { Filename = destination, Password = password });
        var peer = new LiteDatabase(peerEngine);
        using (var warm = peer.BeginTransaction(TimeSpan.FromSeconds(5))) warm.Rollback();
        var child = (SharedEngine)Field(peerEngine, "_cachedTransactionChild");
        var data = new CallbackFile(file);
        var log = new CallbackFile(Path.Combine(directory, name + "-log.db"));
        var outer = new SharedEngine(new EngineSettings
        { Filename = file, Password = password, DataStream = data, LogStream = log });
        var cancel = new CancellationTokenSource();
        var completed = new ManualResetEventSlim();
        Exception? failure = null, nested = null;
        var active = 0;
        var calls = 0;
        var worker = new Thread(() =>
        {
            try
            {
                outer.Insert("rows", new[] { Row(100) }, BsonAutoId.Int32);
                data.Arm(() =>
                {
                    Interlocked.Increment(ref calls);
                    Volatile.Write(ref active, 1);
                    try
                    {
                        using var transaction = peer.BeginTransaction(Timeout.InfiniteTimeSpan, cancel.Token);
                        transaction.GetCollection("rows").Insert(Row(9));
                        transaction.Commit();
                    }
                    catch (Exception error) { nested = error; }
                    finally { Volatile.Write(ref active, 0); }
                });
                outer.Dispose();
            }
            catch (Exception error) { failure = error; }
            finally { completed.Set(); }
        }) { IsBackground = true };
        worker.Start();
        var observed = false;
        var deadline = Stopwatch.StartNew();
        while (!completed.IsSet && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (!otherFile && Volatile.Read(ref active) == 1 && NativeDependency(outer, child))
            {
                Thread.Sleep(50);
                observed = Volatile.Read(ref active) == 1 && NativeDependency(outer, child);
                if (observed) break;
            }
            Thread.Sleep(1);
        }
        if (observed)
        {
            Console.WriteLine(name + ": callback active; child native waiter; outer owner held; independent native acquisition refused");
            cancel.Cancel(); // Diagnostic cleanup, never accepted as a fixed outcome.
        }
        if (!worker.Join(TimeSpan.FromSeconds(10)))
            throw new Exception(name + ": live worker retained; no owner disposal or cold inspection attempted.");
        if (failure != null) throw new Exception(name + ": outer close failed.", failure);
        if (calls != 1) throw new Exception(name + ": checkpoint callback was not reached exactly once.");
        if (otherFile)
        {
            if (nested != null || observed) throw new Exception("Other-file handle control failed.", nested);
        }
        else if (observed)
        {
            if (nested is not OperationCanceledException cancelled || cancelled.CancellationToken != cancel.Token)
                throw new Exception("Native dependency did not drain with the diagnostic cancellation.", nested);
        }
        else if (nested?.GetType() != typeof(InvalidOperationException) || nested.Message != Refusal)
            throw new Exception("Expected the exact prompt handle refusal.", nested);

        using (var after = peer.BeginTransaction(TimeSpan.FromSeconds(5)))
        {
            after.GetCollection("rows").Insert(Row(10));
            after.Commit();
        }
        peer.Dispose(); outer.Dispose(); data.Dispose(); log.Dispose();
        completed.Dispose(); cancel.Dispose();
        Verify(file, password, otherFile ? new[] { 1, 100 } : new[] { 1, 10, 100 });
        if (otherFile) Verify(destination, password, new[] { 1, 9, 10 });
        Console.WriteLine(name + ": cold exact rows, indexed values and sentinel verified");
        return observed;
    }

    private static bool NativeDependency(SharedEngine outer, SharedEngine child)
    {
        if ((int)Field(child, "_mutexWaiters") == 0) return false;
        var owner = Field(outer, "_owner");
        if (!(bool)owner.GetType().GetProperty("IsHeld")!.GetValue(owner)!) return false;
        var mutex = (Mutex)owner.GetType().GetProperty("Mutex")!.GetValue(owner)!;
        // The controller is neither the stream callback nor the native holder.
        if (!mutex.WaitOne(0)) return true;
        mutex.ReleaseMutex();
        return false;
    }

    private static object Field(object value, string name) => value.GetType().GetField(name, Fields)?.GetValue(value)
        ?? throw new Exception("Required production observation unavailable: " + name);

    private static BsonDocument Row(int id) => new() { ["_id"] = id, ["value"] = id, ["payload"] = "row-" + id };

    private static void Seed(string file, string? password)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
        db.GetCollection("rows").Insert(Row(1));
        db.GetCollection("rows").EnsureIndex("value");
        db.GetCollection("sentinel").Insert(Row(42));
    }

    private static void Verify(string file, string? password, int[] ids)
    {
        for (var reopen = 0; reopen < 2; reopen++)
        {
            using var db = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
            var rows = db.GetCollection("rows");
            if (!rows.FindAll().OrderBy(row => row["_id"]).Select(row => row.ToString())
                .SequenceEqual(ids.Select(id => Row(id).ToString()))) throw new Exception("Cold exact state differs.");
            foreach (var id in ids)
            {
                var query = rows.Query().Where(Query.EQ("value", id));
                var plan = query.GetPlan()["index"];
                if (plan["name"] != "value" || !plan["mode"].AsString.StartsWith("INDEX SEEK", StringComparison.Ordinal) ||
                    !query.ToArray().Select(row => row.ToString()).SequenceEqual(new[] { Row(id).ToString() }))
                    throw new Exception("Cold indexed state differs.");
            }
            var sentinel = db.GetCollection("sentinel").FindAll().ToArray();
            if (sentinel.Length != 1 || sentinel[0].ToString() != Row(42).ToString()) throw new Exception("Sentinel changed.");
        }
    }

    private sealed class CallbackFile : FileStream
    {
        private Action? _callback;
        internal CallbackFile(string path) : base(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete) { }
        internal void Arm(Action callback) => Volatile.Write(ref _callback, callback);
        public override void Write(byte[] buffer, int offset, int count)
        { Interlocked.Exchange(ref _callback, null)?.Invoke(); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Interlocked.Exchange(ref _callback, null)?.Invoke(); base.Write(buffer); }
    }
}
