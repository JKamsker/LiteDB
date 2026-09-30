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
    using var entered = new ManualResetEventSlim();
    using var resume = new ManualResetEventSlim();
    var block = false;
    bool reproduced;
    using (var engine = new LiteEngine(new EngineSettings
    {
        Filename = path,
        ReadTransform = (collection, value) =>
        {
            if (block && collection == "rows")
            {
                entered.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("read gate");
            }
            return value;
        }
    }))
    using (var db = new LiteDatabase(engine, disposeOnClose: false))
    {
        Seed(db);
        var tx = db.BeginTransaction();
        tx.GetCollection("rows").Insert(Row(2));
        var rows = tx.GetCollection("rows");
        block = true;
        var reading = Task.Run(() => rows.FindAll().ToArray());
        if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new Exception("read did not enter");
        var closing = Task.Run(db.Dispose);
        Exception? observed = null;
        try
        {
            if (!SpinWait.SpinUntil(() =>
            {
                try { db.GetCollection("sentinel").Count(); return false; }
                catch (ObjectDisposedException) { return true; }
            }, TimeSpan.FromSeconds(5))) throw new Exception("close did not revoke admission");
            try { tx.Dispose(); } catch (Exception error) { observed = error; }
        }
        finally { block = false; resume.Set(); }
        try { reading.GetAwaiter().GetResult(); } catch (ObjectDisposedException) { }
        closing.GetAwaiter().GetResult();
        reproduced = observed is InvalidOperationException;
        if (!reproduced && observed != null) throw new Exception("Unexpected disposal failure", observed);
        if (tx.State != LiteTransactionState.RolledBack) throw new Exception("close did not roll back handle");
    }
    Verify(path, 1);
    return reproduced;
}
