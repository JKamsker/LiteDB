using System.Reflection;
using LiteDB;
using LiteDB.Engine;

internal static class NativeAdmissionHarness
{
    internal static bool TryRun(string mode, string filename, string? password, string[] args)
    {
        if (!mode.StartsWith("native-", StringComparison.Ordinal)) return false;
        var settings = new EngineSettings { Filename = filename, Password = password };
        settings.AllowHostLocalAdmissionFallback = args[4].Contains("fallback", StringComparison.Ordinal);
        if (settings.AllowHostLocalAdmissionFallback)
        {
            var identity = typeof(LiteEngine).Assembly.GetType("LiteDB.Client.Shared.DatabaseFileIdentity")!;
            var prefix = Path.GetFileNameWithoutExtension(filename);
            identity.GetField("UnsupportedVolume", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, (Func<string, bool>)(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)));
        }
        if (args[4].Contains("sha1", StringComparison.Ordinal)) settings.SharedMutexNameStrategy = SharedMutexNameStrategy.Sha1Hash;
        if (mode == "native-pool-hold")
        {
            var connection = new ConnectionString { Filename = filename, Password = password };
            using var first = new LiteDatabase(connection);
            using var second = new LiteDatabase(connection);
            first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 84 });
            first.Dispose();
            if (second.GetCollection("rows").Count() != 2) throw new Exception("Pooled owner lost shared state");
            Console.WriteLine("ready");
            Console.ReadLine();
            return true;
        }
        if (mode == "native-raw-probe")
        {
            var type = typeof(LiteEngine).Assembly.GetType("LiteDB.Client.Shared.DatabaseFileLock")!;
            using var raw = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { filename, true, false, settings.AllowHostLocalAdmissionFallback }, null)!;
            var held = (bool)type.GetMethod("Conflicts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(raw, new object[] { long.MaxValue - 4096, 1L })!;
            if (held != args[4].Contains("held", StringComparison.Ordinal)) throw new Exception("Raw native authority exclusion mismatch");
            Console.WriteLine("done");
            return true;
        }
        var shared = args[4].Contains("shared", StringComparison.Ordinal);
        settings.ReadOnly = args[4].Contains("readonly", StringComparison.Ordinal);
        if (mode == "native-stream-hold")
        {
            using var engine = new SharedEngine(settings);
            using var reader = engine.Query("rows", Query.All());
            if (!reader.Read()) throw new Exception("Streaming reader was empty");
            Console.WriteLine("ready");
            Console.ReadLine();
            var count = 1;
            while (reader.Read())
            {
                var row = reader.Current;
                if (row["value"].AsInt32 != row["_id"].AsInt32 * 2) throw new Exception("Protected snapshot changed");
                count++;
            }
            if (count != 300) throw new Exception("Protected snapshot lost rows");
            return true;
        }
        if (mode == "native-update")
        {
            using var db = new LiteDatabase(new SharedEngine(settings));
            Console.WriteLine("attempting");
            if (!db.GetCollection("rows").Update(new BsonDocument { ["_id"] = 1, ["value"] = 999 }))
                throw new Exception("Contending update missed row");
            db.Checkpoint();
            Console.WriteLine("done");
            return true;
        }
        if (mode == "native-rebuild-hold")
        {
            var stage = args[4].Split('|')[0];
            var rebuild = typeof(LiteEngine).Assembly.GetType("LiteDB.Engine.RebuildService")!;
            rebuild.GetField("SimulateInstallFailure", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, (Action<string>)(actual =>
                {
                    if (actual != stage) return;
                    Console.WriteLine("ready");
                    Console.ReadLine();
                }));
            using var db = new LiteDatabase(shared ? new SharedEngine(settings) : new LiteEngine(settings));
            db.Rebuild();
            Console.WriteLine("installed");
            Console.ReadLine();
            return true;
        }
        try
        {
            using var db = new LiteDatabase(shared ? new SharedEngine(settings) : new LiteEngine(settings));
            if (db.GetCollection("rows").FindById(1)?["value"].AsInt32 != 42)
                throw new Exception("Acknowledged record missing");
            if (mode == "native-rejected") throw new Exception("Incompatible engine was admitted");
            if (mode == "native-write")
            {
                var row = db.GetCollection("rows").FindById(1);
                row["value"] = 84;
                if (!db.GetCollection("rows").Update(row)) throw new Exception("Direct handoff update missed its row");
            }
            if (mode == "native-hold")
            {
                Console.WriteLine("ready");
                Console.ReadLine();
            }
            else Console.WriteLine("done");
        }
        catch (DatabaseAdmissionException) when (mode == "native-rejected") { Console.WriteLine("done"); }
        catch (LiteException e) when (mode == "native-rejected" && e.ErrorCode == LiteException.REBUILD_INCOMPLETE)
        { Console.WriteLine("done"); }
        return true;
    }
}
