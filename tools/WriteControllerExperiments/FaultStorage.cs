using LiteDB;
using LiteDB.Engine;

namespace WriteControllerExperiments;

internal sealed class FaultStorage : IDisposable
{
    internal readonly FileStream Data;
    internal readonly ProbeLog Log;
    private readonly string? _password;

    internal FaultStorage(string file, string? password = null)
    {
        _password = password;
        Data = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        Log = new ProbeLog(Path.ChangeExtension(file, null) + "-log.db");
    }

    internal LiteDatabase Open() => new(new LiteEngine(new EngineSettings
    {
        DataStream = Data, LogStream = Log, Password = _password, DurableCommits = true
    }));

    internal void Seed()
    {
        using var db = Open();
        db.GetCollection("rows").EnsureIndex("value");
        db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "untouched" });
        db.Checkpoint();
    }

    internal void Verify(int expected)
    {
        using var db = Open();
        var rows = db.GetCollection("rows");
        Safety.Check(rows.Count() == expected, "row count");
        for (var id = 1; id <= expected; id++)
        {
            var row = rows.FindById((long)id);
            Safety.Check(row != null && row["value"] == id && row["payload"] == WriteRequest.Payload, "payload");
            Safety.Check(rows.Find(Query.EQ("value", id)).Single()["_id"] == (long)id, "secondary index");
        }
        Safety.Check(db.GetCollection("sentinel").FindById(1)["value"] == "untouched", "sentinel");
    }

    public void Dispose() { Log.Dispose(); Data.Dispose(); }
}

internal sealed class ProbeLog(string path) : FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)
{
    internal int Syncs;
    internal int CommitSyncs;
    internal string? LastSync;
    internal Action? BeforeSync, AfterSync;
    internal string? Failure;
    internal bool Reached;
    internal byte[] Durable = [];

    public override void Flush(bool flushToDisk)
    {
        base.Flush(false);
        if (!flushToDisk) return;
        Interlocked.Increment(ref Syncs);
        LastSync = Environment.StackTrace;
        var commit = LastSync.Contains("DiskService.WriteLogDisk");
        if (commit) Interlocked.Increment(ref CommitSyncs);
        if (commit) BeforeSync?.Invoke();
        if (commit && Failure == "before") { Reached = true; throw new IOException("Injected sync failure"); }
        base.Flush(true);
        Durable = File.ReadAllBytes(Name);
        if (commit) AfterSync?.Invoke();
        if (commit && Failure == "after") { Reached = true; throw new IOException("Injected post-sync failure"); }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (Failure == "partial")
        {
            Reached = true;
            base.Write(buffer, offset, Math.Max(1, count / 2));
            base.Flush(false);
            throw new IOException("Injected partial write / disk full");
        }
        base.Write(buffer, offset, count);
    }
}
