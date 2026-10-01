using System;
using System.IO;
using System.Threading.Tasks;
using LiteDB.Client.Direct;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectPoolFailure_Tests
    {
        [Fact]
        public void Failed_write_stops_all_pooled_owners_and_reopen_preserves_committed_state()
        {
            using var file = new TempFile();
            using (var first = new LiteDatabase(file))
            using (var second = new LiteDatabase(file))
            {
                first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 42 });
                first.GetCollection("rows").EnsureIndex("value", true);
                first.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                first.Checkpoint();
                var engine = NativeAdmissionDirectPool_Tests.Engine(first);
                engine.SimulateDiskWriteFail = page => throw new IOException("pool: injected write failure");
                Assert.Throws<IOException>(() => second.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 84 }));
                Assert.Throws<IOException>(() => first.GetCollection("rows").Count());
                Assert.Throws<DatabaseAdmissionException>(() => new LiteDatabase(file));
            }
            for (var i = 0; i < 2; i++)
            {
                using var cold = new LiteDatabase(file);
                Assert.Equal(1, cold.GetCollection("rows").Count());
                Assert.Equal(42, cold.GetCollection("rows").FindOne("value = 42")["value"].AsInt32);
                Assert.NotNull(cold.GetCollection("sentinel").FindById(9));
            }
        }

        [Theory]
        [InlineData("before-log-backup")]
        [InlineData("after-source-backup")]
        [InlineData("after-temp-install")]
        public void Failed_replacement_cannot_resurrect_a_stopped_pooled_engine(string stage)
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using (var first = new LiteDatabase(file))
            using (var second = new LiteDatabase(file))
            {
                var reached = false;
                RebuildService.SimulateInstallFailure = actual =>
                {
                    if (actual != stage) return;
                    reached = true;
                    throw new IOException("pool: replacement failure");
                };
                try { Assert.Throws<IOException>(() => first.Rebuild()); }
                finally { RebuildService.SimulateInstallFailure = null; }
                Assert.True(reached);
                Assert.True(NativeAdmissionDirectPool_Tests.Engine(second).IsDisposed);
                Assert.Throws<DatabaseAdmissionException>(() => new LiteDatabase(file));
            }
            NativeAdmission_Tests.Verify(file);
            NativeAdmission_Tests.Verify(file);
        }

        [Fact]
        public void Read_transform_can_wait_for_foreign_reader_disposal_without_releasing_its_active_call()
        {
            using var file = new TempFile();
            IBsonDataReader reader = null;
            var disposedDuringRead = false;
            var connection = new ConnectionString { Filename = file };
            using var db = new LiteDatabase(connection.CreateEngine(settings => settings.ReadTransform = (collection, value) =>
            {
                if (reader != null)
                {
                    Assert.True(Task.Run(() => reader.Dispose()).Wait(TimeSpan.FromSeconds(5)));
                    Assert.True(NativeAdmissionDirectPool_Tests.Locked(file));
                    disposedDuringRead = true;
                }
                return value;
            }));
            for (var i = 1; i <= 4; i++) db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = i });
            using (reader = db.Execute("SELECT $ FROM rows"))
            {
                db.Dispose();
                while (!disposedDuringRead && reader.Read()) { }
                Assert.True(disposedDuringRead);
            }
            Assert.False(NativeAdmissionDirectPool_Tests.Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.Equal(4, cold.GetCollection("rows").Count());
        }
    }
}
