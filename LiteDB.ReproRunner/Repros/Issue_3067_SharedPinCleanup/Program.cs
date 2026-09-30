using System.Runtime.InteropServices;
using LiteDB;
using LiteDB.Engine;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

namespace Issue_3067_SharedPinCleanup;

internal static class Program
{
    private static int Main()
    {
        var host = ReproHostClient.CreateDefault();
        ReproConfigurationReporter.SendConfiguration(host);
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The file-size-limit proof requires Linux.");
            var directory = Path.Combine(Path.GetTempPath(), "p133-pin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var reproduced = Reproduce(Path.Combine(directory, "data.db"));
            host.SendResult(reproduced, reproduced ? "The bug reproduced." : "The bug did not reproduce.");
            return reproduced ? 0 : 10;
        }
        catch (Exception error)
        {
            host.SendResult(false, "The reproduction failed unexpectedly.", new { Error = error.ToString() });
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static bool Reproduce(string filename)
    {
        var db = new LiteDatabase(new ConnectionString { Filename = filename, Connection = ConnectionType.Shared });
        db.CheckpointSize = 0;
        var rows = db.GetCollection("rows");
        rows.EnsureIndex("value");
        rows.Insert(Enumerable.Range(0, 150).Select(i => new BsonDocument
            { ["_id"] = i, ["value"] = i, ["payload"] = new string('x', 4000) }));
        var reader = db.Execute("SELECT $ FROM rows");
        if (!reader.Read()) throw new InvalidOperationException("Streaming snapshot did not open.");
        rows.Insert(new BsonDocument { ["_id"] = 200, ["value"] = 200 });
        db.CheckpointSize = 10000;
#pragma warning disable CS0618
        if (!db.BeginTrans()) throw new InvalidOperationException("Pinned legacy transaction did not begin.");
#pragma warning restore CS0618
        rows.Insert(new BsonDocument { ["_id"] = 201, ["value"] = 201 });
        var leases = Directory.GetFiles(filename + "-readers", "*.lease");
        if (leases.Length == 0) throw new InvalidOperationException("No live snapshot lease.");
        Exception? closeError = null;
        // The close checkpoint must append its header recovery record before writing
        // data. Cap file growth at the existing WAL length to fail that append, with
        // existing committed frames intact. This is a real OS I/O fault, not a hook.
        var log = Path.Combine(Path.GetDirectoryName(filename)!, "data-log.db");
        using (new FileLimit((ulong)new FileInfo(log).Length))
        {
            try { db.Dispose(); }
            catch (Exception error) { closeError = error; }
        }
        if (closeError is not IOException && closeError is not ArgumentOutOfRangeException { ParamName: "value" }) throw new InvalidOperationException("Pin close did not surface the expected OS write failure.", closeError);
        var count = 1;
        while (reader.Read()) count++;
        if (count != 150) throw new InvalidOperationException("Close damaged the snapshot.");
        reader.Dispose();
        db.Dispose();
        var leaked = leases.Any(IsStillHeld);
        if (!leaked)
        {
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(filename);
                var actual = cold.GetCollection("rows").Find(Query.GTE("value", 0)).Select(row => row["_id"].AsInt32).OrderBy(id => id);
                if (!actual.SequenceEqual(Enumerable.Range(0, 150).Concat(new[] { 200 })))
                    throw new InvalidOperationException("Cold reopen disagrees with the indexed committed model.");
            }
        }
        GC.KeepAlive(db);
        return leaked;
    }

    private static bool IsStillHeld(string path)
    {
        try { using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; }
    }

    private sealed class FileLimit : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Limit { internal ulong Current, Maximum; }
        [DllImport("libc", SetLastError = true)] private static extern int getrlimit(int resource, out Limit limit);
        [DllImport("libc", SetLastError = true)] private static extern int setrlimit(int resource, ref Limit limit);
        [DllImport("libc", SetLastError = true)] private static extern IntPtr signal(int signum, IntPtr handler);
        private Limit _original;
        private readonly IntPtr _signal;
        internal FileLimit(ulong size)
        {
            if (getrlimit(1, out _original) != 0) throw new IOException("getrlimit failed");
            _signal = signal(25, new IntPtr(1));
            if (_signal == new IntPtr(-1)) throw new IOException("signal failed");
            var changed = new Limit { Current = Math.Min(size, _original.Maximum), Maximum = _original.Maximum };
            if (setrlimit(1, ref changed) != 0) { signal(25, _signal); throw new IOException("setrlimit failed"); }
        }
        public void Dispose()
        {
            var failed = setrlimit(1, ref _original) != 0;
            signal(25, _signal);
            if (failed) throw new IOException("Failed to restore RLIMIT_FSIZE");
        }
    }
}
