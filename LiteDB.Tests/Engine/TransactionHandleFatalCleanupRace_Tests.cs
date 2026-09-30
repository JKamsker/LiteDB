using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleFatalCleanupRace_Tests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        public void Cleanup_discriminates_published_peer_failure_from_its_own_failure(bool peer, bool invalidData, bool distinctFailure)
        {
            using var file = new TempFile();
            using (var engine = new LiteEngine(file.Filename))
            using (var db = new LiteDatabase(engine, disposeOnClose: false))
            {
                var rows = db.GetCollection("rows");
                rows.Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
                rows.EnsureIndex("value");
                db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                db.Checkpoint();
                var tx = db.BeginTransaction();
                // New pages force rollback's free-page publication through WAL I/O.
                tx.GetCollection("other").Insert(new BsonDocument { ["_id"] = 2, ["padding"] = new string('x', 20000) });
                var state = (EngineState)typeof(LiteEngine).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(engine);
                Exception original = invalidData ? (Exception)new LiteException(LiteException.INVALID_DATAFILE_STATE, "cleanup fault") :
                    new IOException("cleanup fault");
                var reached = false;
                engine.SimulateDiskWriteFail = page =>
                {
                    reached = true;
                    engine.SimulateDiskWriteFail = null;
                    if (peer)
                    {
                        // This callback is after cleanup admission. Publish the peer's
                        // fatal latch and throw exactly what a later state check throws.
                        state.Stop(original);
                        state.Validate();
                    }
                    if (distinctFailure) state.Stop(new IOException("independent peer failure"));
                    throw original;
                };
                if (peer)
                {
                    tx.Dispose();
                    Assert.Equal(LiteTransactionState.RolledBack, tx.State);
                }
                else
                {
                    Assert.Same(original, Record.Exception(tx.Dispose));
                    Assert.Equal(LiteTransactionState.Failed, tx.State);
                }
                Assert.True(reached);
                tx.Dispose();
            }
            for (var reopen = 0; reopen < 2; reopen++)
            {
                using var db = new LiteDatabase(file);
                var query = db.GetCollection("rows").Query().Where(Query.EQ("value", 10));
                Assert.Equal("value", query.GetPlan()["index"]["name"].AsString);
                Assert.StartsWith("INDEX SEEK", query.GetPlan()["index"]["mode"].AsString);
                Assert.Equal(new[] { 1 }, query.ToArray().Select(row => row["_id"].AsInt32));
                Assert.Equal(1, db.GetCollection("rows").Count());
                Assert.Equal(0, db.GetCollection("other").Count());
                Assert.NotNull(db.GetCollection("sentinel").FindById(9));
            }
        }
    }
}
