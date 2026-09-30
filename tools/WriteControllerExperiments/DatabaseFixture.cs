using LiteDB;

namespace WriteControllerExperiments;

internal static class DatabaseFixture
{
    internal static LiteDatabase Open(string file, bool shared = false) => new(new ConnectionString
    {
        Filename = file, Connection = shared ? ConnectionType.Shared : ConnectionType.Direct, DurableCommits = true
    });

    internal static void Seed(string file)
    {
        using var db = Open(file);
        db.GetCollection("rows").EnsureIndex("value");
        db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "untouched" });
        db.Checkpoint();
    }

    internal static void Verify(string file, int[] counts)
    {
        using var db = Open(file);
        var rows = db.GetCollection("rows");
        var seen = new HashSet<long>();
        foreach (var row in rows.FindAll())
        {
            var id = row["_id"].AsInt64;
            var worker = (int)(id / 1000000000);
            var sequence = (int)(id % 1000000000);
            if (worker < 0 || worker >= counts.Length || sequence < 1 || sequence > counts[worker] ||
                row["value"].AsInt32 != sequence || row["payload"].AsString != WriteRequest.Payload || !seen.Add(id))
                throw new Exception("Payload, model, or uniqueness mismatch.");
        }
        if (seen.Count != counts.Sum() || rows.Find(Query.GTE("value", 1)).Count() != seen.Count ||
            db.GetCollection("sentinel").FindById(1)["value"] != "untouched")
            throw new Exception("Missing acknowledged data, incorrect index, or changed sentinel.");
    }
}
