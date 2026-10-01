using System.Reflection;
using LiteDB.Engine;

namespace LiteDB.Fuzz.Targets;

internal static class SharedLifecycleChild
{
    internal static int Run(FuzzOptions options)
    {
        try
        {
            var settings = new EngineSettings
            {
                Filename = options.Database, Password = options.WorkerId == 1 ? "lifecycle-secret" : null,
                DurableCommits = true, TransactionPageLimit = 4, ReadTransform = (_, value) => value
            };
            var engine = new SharedEngine(settings);
            using var db = new LiteDatabase(engine);
            ILiteTransaction transaction = null;
            IBsonDataReader reader = null;
            object cached = null;
            SharedCheckpointCrashScope checkpointScope = null;
            var crashPhase = "";
            var waitObserved = 0;
            TransactionAdmission.Observe = stage =>
            {
                if (stage == "native-wait" && Interlocked.Exchange(ref waitObserved, 1) == 0)
                { Mark(stage); Say(stage); }
            };
            EngineState.SimulateProcessCrash = phase =>
            {
                if (phase != crashPhase) return;
                Boundary("hook:" + phase);
            };
            Say("ready");
            string command;
            while ((command = Console.ReadLine()) != null)
            {
                waitObserved = 0;
                switch (command)
                {
                    case "warm":
                        using (var warm = db.BeginTransaction()) warm.Rollback();
                        cached = Cached(engine);
                        if (cached == null) throw new InvalidOperationException("No cached Shared wrapper.");
                        Say("warmed");
                        break;
                    case "begin":
                        transaction = db.BeginTransaction();
                        if (cached != null && Cached(engine) != null)
                            throw new InvalidOperationException("Cached wrapper was not checked out.");
                        var rows = transaction.GetCollection("rows");
                        rows.Update(new BsonDocument { ["_id"] = 2, ["value"] = 202, ["payload"] = "changed" });
                        rows.Delete(3);
                        foreach (var id in new[] { 10, 11 }) rows.Insert(SharedLifecycleOracle.Document(id));
                        Say("begun");
                        break;
                    case "commit":
                        transaction.Commit(); transaction.Dispose(); transaction = null;
                        if (cached != null && !ReferenceEquals(cached, Cached(engine)))
                            throw new InvalidOperationException("Shared wrapper was not reused.");
                        Say("committed");
                        break;
                    case "refresh":
                        using (var refreshed = db.BeginTransaction())
                        {
                            if (refreshed.GetCollection("rows").FindById(20) == null ||
                                refreshed.GetCollection("rows").FindById(21) == null)
                                throw new InvalidOperationException("Reused wrapper missed the external commit.");
                            refreshed.Commit();
                        }
                        if (!ReferenceEquals(cached, Cached(engine)))
                            throw new InvalidOperationException("Refreshed handle did not reuse the wrapper.");
                        Say("refreshed"); break;
                    case "write":
                        using (var tx = db.BeginTransaction())
                        {
                            tx.GetCollection("rows").Insert(SharedLifecycleOracle.Document(20));
                            tx.GetCollection("rows").Insert(SharedLifecycleOracle.Document(21));
                            tx.Commit();
                        }
                        Say("written");
                        break;
                    case "snapshot":
                        reader = db.Execute("SELECT $ FROM rows");
                        AssertSnapshot(engine, reader);
                        if (!reader.Read() || reader.Current["_id"].AsInt32 != 1)
                            throw new InvalidOperationException("Snapshot sentinel missing.");
                        Say("snapshot-held");
                        break;
                    case "retire":
                        var original = new List<BsonDocument> { reader.Current.AsDocument };
                        while (reader.Read()) original.Add(reader.Current.AsDocument);
                        if (original.Count != 3 || original.OrderBy(row => row["_id"]).Select(row => row.ToString())
                            .SequenceEqual(new[] { 1, 2, 3 }.Select(id => SharedLifecycleOracle.Document(id).ToString())) == false)
                            throw new InvalidOperationException("Held snapshot changed across external commit.");
                        reader.Dispose(); reader = null; Say("retired"); break;
                    case "read":
                        if (db.GetCollection("sentinel").FindById(99)?["value"].AsInt32 != 999)
                            throw new InvalidOperationException("Sentinel changed.");
                        Say("read"); break;
                    case "reopen":
                        using (var peer = new LiteDatabase(new ConnectionString
                        { Filename = options.Database, Password = settings.Password, Connection = ConnectionType.Shared }))
                            if (peer.GetCollection("sentinel").Count() != 1) throw new InvalidOperationException("Open lost sentinel.");
                        Say("reopened"); break;
                    case "prepare-checkpoint":
                        checkpointScope = SharedCheckpointCrashScope.Create(engine, "checkpoint-before-data-flush");
                        db.BeginTrans();
                        db.GetCollection("rows").Insert(SharedLifecycleOracle.Document(20));
                        db.GetCollection("rows").Insert(SharedLifecycleOracle.Document(21));
                        db.Commit(); Say("written"); break;
                    case "checkpoint": db.Checkpoint(); Say("checkpointed"); break;
                    case "close": db.Dispose(); Say("disposed"); break;
                    case "exit": checkpointScope?.Dispose(); reader?.Dispose(); transaction?.Dispose(); db.Dispose(); Say("closed"); return 0;
                    default:
                        if (command.StartsWith("arm:")) { crashPhase = command[4..]; Say("armed"); }
                        else if (command.StartsWith("stop:")) Boundary(command);
                        else throw new InvalidOperationException("Unknown command " + command);
                        break;
                }
            }
            throw new InvalidOperationException("Parent closed the protocol without exit.");

            void Mark(string phase)
            {
                using var stream = new FileStream(options.Ledger, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(phase); writer.Flush(); stream.Flush(true);
            }
            void Boundary(string phase)
            {
                Mark(phase);
                Say(phase);
                Thread.Sleep(Timeout.Infinite);
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }

    private static void AssertSnapshot(SharedEngine engine, IBsonDataReader reader)
    {
        var snapshot = reader.GetType().GetField("_ownedSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        if (snapshot == null || snapshot.GetValue(reader) == null)
            throw new InvalidOperationException("Reader did not retain an independent snapshot.");
        var readers = (Dictionary<int, int>)typeof(SharedEngine)
            .GetField("_localReaders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine);
        if (readers.Count != 1 || readers.Values.Single() != 1 || engine.MutexOwner.IsOwnedByCurrentThread)
            throw new InvalidOperationException("Snapshot lease publication/native release was not observed.");
    }

    private static object Cached(SharedEngine engine) => typeof(SharedEngine)
        .GetField("_cachedTransactionChild", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine);
    private static void Say(string value) { Console.WriteLine(value); Console.Out.Flush(); }
}
