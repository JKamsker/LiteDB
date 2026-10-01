using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LiteDB;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;

var host = ReproHostClient.CreateDefault();
ReproConfigurationReporter.SendConfiguration(host);
var root = ReproContext.FromEnvironment().SharedDatabaseRoot ?? AppContext.BaseDirectory;
Directory.CreateDirectory(root);
var path = Path.Combine(root, "callback-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    var reproduced = Run(path);
    Console.WriteLine(reproduced ? "REGRESSION_REPRODUCED" : "FIXED_STATE_VERIFIED");
    host.SendResult(reproduced, reproduced ? "Callback waited for its own handle's lock." : "Callback self-wait refused; independent work and cold state verified.");
    return reproduced ? 0 : 10;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    host.SendResult(false, "Unexpected or ambiguous result: " + error);
    return 2;
}

static BsonDocument Row(int id) => new BsonDocument { ["_id"] = id, ["Value"] = id * 10 };
static bool Run(string path)
{
    bool reproduced;
    using (var db = new LiteDatabase(path))
    {
        db.GetCollection("rows").Insert(Row(1));
        db.GetCollection("rows").EnsureIndex("Value");
        db.GetCollection("sentinel").Insert(Row(9));
        db.Timeout = TimeSpan.FromSeconds(4);
        using var tx = db.BeginTransaction();
        tx.GetCollection("rows").Insert(Row(2));
        var elapsed = TimeSpan.Zero;
        var reached = false;
        tx.GetCollection<CallbackRow>("rows").Insert(new CallbackRow { Id = 3, Callback = () =>
        {
            reached = true;
            var timer = Stopwatch.StartNew();
            try { db.GetCollection("rows").Insert(Row(4)); throw new Exception("Self-dependent write unexpectedly succeeded"); }
            catch (LiteException error) when (error.ErrorCode == LiteException.LOCK_TIMEOUT) { }
            elapsed = timer.Elapsed;
            if (db.GetCollection("rows").FindById(2) != null) throw new Exception("Ordinary read enlisted in handle");
            db.GetCollection("ordinary").Insert(Row(5));
        }});
        if (!reached || tx.State != LiteTransactionState.Active) throw new Exception("Callback did not preserve handle");
        reproduced = elapsed >= TimeSpan.FromSeconds(3);
        if (!reproduced && elapsed >= TimeSpan.FromSeconds(1)) throw new Exception("Ambiguous callback latency " + elapsed);
        Console.WriteLine("Observed callback refusal after " + elapsed);
        tx.Rollback();
    }
    for (var reopen = 0; reopen < 2; reopen++)
    {
        using var db = new LiteDatabase(path);
        var query = db.GetCollection("rows").Query().Where(Query.EQ("Value", 10));
        if (query.GetPlan()["index"]["name"] != "Value" ||
            !query.ToArray().Select(row => row["_id"].AsInt32).SequenceEqual(new[] { 1 }) ||
            db.GetCollection("rows").Count() != 1 || db.GetCollection("ordinary").FindById(5) == null ||
            db.GetCollection("sentinel").FindById(9) == null)
            throw new Exception("Cold state/index model mismatch");
    }
    return reproduced;
}
sealed class CallbackRow
{
    [BsonIgnore] public Action? Callback;
    public int Id { get; set; }
    public int Value { get { Callback?.Invoke(); return Id * 10; } set { } }
}
