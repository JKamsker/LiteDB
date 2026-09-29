using LiteDB;
using LiteDB.Engine;
using System.Reflection;
using System.Collections;
var scenario = args[0];
if (scenario == "binary")
{
    using ILiteDatabase db = new LiteDatabase(":memory:");
    if (!db.BeginTrans()) throw new Exception("begin failed");
    db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
    if (!db.Commit() || db.GetCollection("rows").Count() != 1) throw new Exception("commit failed");
    using var mock = new MockDatabase();
    using var raw = new LiteEngine(new EngineSettings { Filename = ":memory:" });
    using var decorated = new LiteDatabase(new LegacyEngineDecorator(raw), disposeOnClose: false);
    if (!decorated.BeginTrans()) throw new Exception("decorator begin failed");
    decorated.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
    if (!decorated.Commit() || decorated.GetCollection("rows").Count() != 1) throw new Exception("decorator failed");
    // Reflection keeps this fixture compilable against the actual parent interface/API.
    var extension = typeof(LiteDatabase).Assembly.GetType("LiteDB.LiteTransactionExtensions")?.GetMethod("BeginTransaction");
    if (extension != null)
    {
        foreach (ILiteDatabase unsupported in new ILiteDatabase[] { mock, decorated })
        {
            try { extension.Invoke(null, new object[] { unsupported }); throw new Exception("unsupported capability accepted"); }
            catch (TargetInvocationException error) when (error.InnerException is NotSupportedException) { }
        }
        if (!raw.BeginTrans()) throw new Exception("capability rejection started a legacy transaction");
        raw.Rollback();
    }
    Console.WriteLine("PASS precompiled old ILiteDatabase implementation and legacy consumer");
    return;
}
using var engine = new LiteEngine(new EngineSettings { Filename = ":memory:" });
using var database = new LiteDatabase(engine, disposeOnClose: false);
database.BeginTrans();
var original = new InvalidOperationException("original input error");
var cleanup = new IOException("rollback cleanup error");
var monitor = typeof(LiteEngine).GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(engine);
var hook = monitor.GetType().GetProperty("AfterTransactionExit", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
hook.SetValue(monitor, (Action)(() => { hook.SetValue(monitor, null); throw cleanup; }));
IEnumerable<BsonDocument> Input() { yield return new BsonDocument { ["_id"] = 1 }; throw original; }
try { database.GetCollection("rows").Insert(Input()); throw new Exception("expected failure"); }
catch (Exception actual)
{
    Console.WriteLine($"primary={actual.Message}; attached={actual.Data.Count}");
    if (!ReferenceEquals(actual, original) || !actual.Data.Values.Cast<object>().Contains(cleanup)) Environment.Exit(17);
}
Console.WriteLine("PASS original error preserved with cleanup failure");
