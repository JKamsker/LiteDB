using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using LiteDB.Engine;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

var host = ReproHostClient.CreateDefault();
ReproConfigurationReporter.SendConfiguration(host);
var root = ReproContext.FromEnvironment().SharedDatabaseRoot ?? AppContext.BaseDirectory;
Directory.CreateDirectory(root);
var path = Path.Combine(root, "case-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    var reproduced = Run(path);
    Console.WriteLine(reproduced ? "REGRESSION_REPRODUCED" : "FIXED_STATE_VERIFIED");
    host.SendResult(reproduced, reproduced ? "Reported disposal regression reproduced." : "Disposal preserved outcome and cold state.");
    return reproduced ? 0 : 10;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    host.SendResult(false, "Unexpected harness failure: " + error);
    return 2;
}

static BsonDocument Row(int id) => new BsonDocument { ["_id"] = id, ["value"] = id * 10 };
static void Seed(LiteDatabase db)
{
    db.GetCollection("rows").Insert(Row(1));
    db.GetCollection("rows").EnsureIndex("value");
    db.GetCollection("sentinel").Insert(Row(9));
    db.Checkpoint();
}
static void Verify(string path, params int[] ids)
{
    for (var i = 0; i < 2; i++)
    {
        using var cold = new LiteDatabase(path);
        var rows = cold.GetCollection("rows");
        if (!rows.FindAll().Select(x => x["_id"].AsInt32).OrderBy(x => x).SequenceEqual(ids) ||
            !rows.Find(Query.GTE("value", 10)).Select(x => x["_id"].AsInt32).OrderBy(x => x).SequenceEqual(ids) ||
            cold.GetCollection("other").Count() != 0 || cold.GetCollection("sentinel").FindById(9)["value"] != 90)
            throw new Exception("Cold record/index/sentinel state disagrees with model");
    }
}
static bool Run(string path)
{
    bool reproduced;
    using (var data = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
    using (var log = new FailingLog(Path.ChangeExtension(path, null) + "-log.db"))
    using (var engine = new LiteEngine(new EngineSettings { DataStream = data, LogStream = log, DurableCommits = true }))
    using (var db = new LiteDatabase(engine, disposeOnClose: false))
    {
        Seed(db);
        var healthy = db.BeginTransaction();
        healthy.GetCollection("other").Insert(Row(3));
        using var failed = db.BeginTransaction();
        failed.GetCollection("rows").Insert(Row(2));
        log.Fail = true;
        try { failed.Commit(); throw new Exception("Expected injected peer write failure"); }
        catch (IOException error) when (ReferenceEquals(error, log.Failure)) { }
        if (!log.Reached || failed.State != LiteTransactionState.Indeterminate)
            throw new Exception("Peer fault did not hit expected WAL transition");
        Exception? observed = null;
        try { healthy.Dispose(); } catch (Exception error) { observed = error; }
        reproduced = observed is IOException io && ReferenceEquals(io.InnerException, log.Failure);
        if (!reproduced && (observed != null || healthy.State != LiteTransactionState.RolledBack))
            throw new Exception("Unexpected healthy disposal outcome", observed);
        log.Fail = false;
    }
    Verify(path, 1);
    return reproduced;
}
sealed class FailingLog : FileStream
{
    internal bool Fail, Reached;
    internal readonly IOException Failure = new IOException("peer WAL failure");
    internal FailingLog(string path) : base(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite) { }
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (Fail) { Reached = true; throw Failure; }
        base.Write(buffer, offset, count);
    }
}
