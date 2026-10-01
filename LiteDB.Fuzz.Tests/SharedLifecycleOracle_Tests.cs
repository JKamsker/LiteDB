using LiteDB.Fuzz.Targets;
using Xunit;

namespace LiteDB.Fuzz.Tests;

public sealed class SharedLifecycleOracle_Tests
{
    [Theory]
    [InlineData("ack-loss")]
    [InlineData("partial-insert")]
    [InlineData("partial-update")]
    [InlineData("partial-delete")]
    [InlineData("sentinel")]
    [InlineData("index")]
    [InlineData("peer-partial")]
    public void Independent_model_rejects_controlled_bad_states(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "litedb-lifecycle-oracle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "oracle.db");
            SharedLifecycleOracle.Seed(path, null);
            using (var db = new LiteDatabase(path))
            {
                var rows = db.GetCollection("rows");
                switch (mutation)
                {
                    case "partial-insert": rows.Insert(SharedLifecycleOracle.Document(10)); break;
                    case "partial-update": rows.Update(new BsonDocument { ["_id"] = 2, ["value"] = 202, ["payload"] = "changed" }); break;
                    case "partial-delete": rows.Delete(3); break;
                    case "sentinel": db.GetCollection("sentinel").Delete(99); break;
                    case "index": rows.DropIndex("value"); break;
                    case "peer-partial": rows.Insert(SharedLifecycleOracle.Document(20)); break;
                }
            }
            using var context = new FuzzContext("shared-lifecycle", 1, 1, null, directory);
            Assert.Throws<FuzzFailureException>(() => SharedLifecycleOracle.Verify(context, path, null,
                mutation == "ack-loss" ? true : null, null));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_outcome_accepts_only_a_complete_before_or_after_group(bool commit)
    {
        var directory = Path.Combine(Path.GetTempPath(), "litedb-lifecycle-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "oracle.db");
            SharedLifecycleOracle.Seed(path, null);
            if (commit)
            {
                using var db = new LiteDatabase(path);
                using var tx = db.BeginTransaction();
                var rows = tx.GetCollection("rows");
                rows.Update(new BsonDocument { ["_id"] = 2, ["value"] = 202, ["payload"] = "changed" });
                rows.Delete(3);
                rows.Insert(SharedLifecycleOracle.Document(10)); rows.Insert(SharedLifecycleOracle.Document(11));
                tx.Commit();
            }
            using var context = new FuzzContext("shared-lifecycle", 1, 1, null, directory);
            var result = SharedLifecycleOracle.Verify(context, path, null, null, false);
            Assert.Equal(commit, result.Owner);
            Assert.False(result.Peer);
        }
        finally { Directory.Delete(directory, true); }
    }
}
