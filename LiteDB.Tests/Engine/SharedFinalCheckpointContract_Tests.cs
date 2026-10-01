using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class SharedFinalCheckpointContract_Tests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("secret", false)]
        [InlineData("secret", true)]
        public void Parent_final_checkpoint_remains_best_effort_and_failed_attempt_keeps_committed_wal(string password, bool fail)
        {
            using var file = new TempFile();
            var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password });
            var db = new LiteDatabase(shared);
            using (var transaction = db.BeginTransaction())
            {
                var rows = transaction.GetCollection("rows");
                rows.EnsureIndex("value");
                rows.Insert(Enumerable.Range(0, 32).Select(i => new BsonDocument { ["_id"] = i, ["value"] = i }));
                transaction.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 7, ["value"] = "untouched" });
                transaction.Commit();
                Assert.Equal(LiteTransactionState.Committed, transaction.State);
            }
            // The child already completed; only the parent's fresh final-checkpoint
            // core can reach this hook. Its normal threshold left a nonempty WAL.
            Assert.Equal(0, shared.EngineOpens);
            var log = FileHelper.GetLogFile(file.Filename);
            var wal = TempFile.ReadAllBytesShared(log);
            Assert.NotEmpty(wal);
            var settings = (EngineSettings)typeof(SharedEngine)
                .GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shared);
            var attempts = 0;
            settings.CheckpointStage = stage =>
            {
                if (stage != "before-index-lock") return;
                attempts++;
                if (fail) throw new IOException("parent final checkpoint failed");
            };
            Assert.Null(Record.Exception(db.Dispose));
            Assert.Equal(1, shared.EngineOpens);
            Assert.Equal(1, attempts);
            db.Dispose();
            if (fail) Assert.Equal(wal, TempFile.ReadAllBytesShared(log));
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                var indexed = cold.GetCollection("rows").Query().Where("value >= 0");
                Assert.Equal("value", indexed.GetPlan()["index"]["name"].AsString);
                Assert.StartsWith("INDEX SCAN", indexed.GetPlan()["index"]["mode"].AsString);
                var seek = cold.GetCollection("rows").Query().Where(Query.EQ("value", 0));
                Assert.StartsWith("INDEX SEEK", seek.GetPlan()["index"]["mode"].AsString);
                Assert.Equal(0, Assert.Single(seek.ToArray())["_id"].AsInt32);
                Assert.Equal(Enumerable.Range(0, 32), indexed.ToEnumerable().Select(row => row["_id"].AsInt32).OrderBy(id => id));
                Assert.Equal("untouched", cold.GetCollection("sentinel").FindById(7)["value"].AsString);
                cold.Checkpoint();
            }
            GC.KeepAlive(db);
        }
    }
}
