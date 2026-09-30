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
    using (var db = new LiteDatabase(path))
    {
        Seed(db);
        using var tx = db.BeginTransaction();
        tx.GetCollection("rows").Insert(Row(2));
        var reader = tx.GetCollection("rows").Query().ExecuteReader();
        reader.Read();
        reader.Dispose();
        Exception? observed = null;
        try { reader.Read(); } catch (Exception error) { observed = error; }
        reproduced = observed is NullReferenceException && tx.State == LiteTransactionState.Failed;
        if (!reproduced)
        {
            if (observed is not ObjectDisposedException || tx.State != LiteTransactionState.Active)
                throw new Exception("Unexpected disposed-reader behavior", observed);
            var iterator = tx.GetCollection("rows").FindAll().GetEnumerator();
            iterator.MoveNext();
            iterator.Dispose();
            try { iterator.MoveNext(); throw new Exception("Disposed iterator accepted use"); }
            catch (ObjectDisposedException) { }
            if (tx.State != LiteTransactionState.Active) throw new Exception("Iterator misuse aborted handle");
            tx.Commit();
        }
    }
    Verify(path, reproduced ? new[] { 1 } : new[] { 1, 2 });
    return reproduced;
}
