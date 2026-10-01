namespace LiteDB.Fuzz.Targets;

internal static class SharedLifecycleOracle
{
    internal static BsonDocument Document(int id) => new()
    { ["_id"] = id, ["value"] = id * 10, ["payload"] = "row-" + id };

    internal static void Seed(string path, string password)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = path, Password = password });
        foreach (var id in new[] { 1, 2, 3 }) db.GetCollection("rows").Insert(Document(id));
        db.GetCollection("rows").EnsureIndex("value", true);
        db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 99, ["value"] = 999 });
        db.Checkpoint();
    }

    internal static (bool Owner, bool Peer) Verify(FuzzContext context, string path, string password, bool? committed, bool? peer)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = path, Password = password });
        var actual = db.GetCollection("rows").FindAll().OrderBy(row => row["_id"]).ToArray();
        var permitted = (from ownerState in new[] { false, true }
            from peerState in new[] { false, true }
            where (!committed.HasValue || committed == ownerState) && (!peer.HasValue || peer == peerState)
            select (Owner: ownerState, Peer: peerState, Rows: Model(ownerState, peerState))).ToArray();
        var matching = permitted.Where(state => Equal(actual, state.Rows)).ToArray();
        context.Check(matching.Length == 1,
            "Cold state lost an acknowledgement, exposed a partial/forbidden transaction or changed a payload.");
        var resolved = matching.Single();
        var expected = resolved.Rows;
        context.Check(db.GetCollection("$indexes").FindOne("collection = 'rows' AND name = 'value'") != null,
            "The expected secondary index was dropped.");
        var query = db.GetCollection("rows").Query().Where("value >= 0");
        var plan = query.GetPlan()["index"];
        context.Check(plan["name"].AsString == "value" && plan["mode"].AsString.StartsWith("INDEX SCAN"),
            "The model comparison did not exercise the secondary index.");
        var indexed = query.ToArray().OrderBy(row => row["value"]).ToArray();
        context.Check(Equal(indexed, expected.OrderBy(row => row["value"]).ToArray()), "Secondary index disagrees with model.");
        foreach (var row in expected)
        {
            var seek = db.GetCollection("rows").Query().Where(Query.EQ("value", row["value"]));
            context.Check(seek.GetPlan()["index"]["name"] == "value" &&
                seek.GetPlan()["index"]["mode"].AsString.StartsWith("INDEX SEEK"), "Expected indexed seek was not used.");
            context.Check(Equal(seek.ToArray(), new[] { row }), "Secondary seek lost or changed a document.");
        }
        var sentinel = db.GetCollection("sentinel").FindAll().ToArray();
        context.Check(sentinel.Length == 1 && sentinel[0]["_id"] == 99 && sentinel[0]["value"] == 999,
            "Unrelated sentinel changed.");
        db.Checkpoint();
        return (resolved.Owner, resolved.Peer);
    }

    private static BsonDocument[] Model(bool committed, bool peer)
    {
        var rows = new List<BsonDocument> { Document(1), Document(2), Document(3) };
        if (committed)
        {
            rows.RemoveAll(row => row["_id"] == 2 || row["_id"] == 3);
            rows.Add(new BsonDocument { ["_id"] = 2, ["value"] = 202, ["payload"] = "changed" });
            rows.Add(Document(10)); rows.Add(Document(11));
        }
        if (peer) { rows.Add(Document(20)); rows.Add(Document(21)); }
        return rows.OrderBy(row => row["_id"]).ToArray();
    }

    private static bool Equal(BsonDocument[] actual, BsonDocument[] expected) => actual.Length == expected.Length &&
        actual.Zip(expected).All(pair => pair.First.ToString() == pair.Second.ToString());
}
