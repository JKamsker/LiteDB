#if !NETFRAMEWORK
using System;
using System.Linq;
using System.Threading.Tasks;
using LiteDB.Internals;
using Xunit;

namespace LiteDB.Tests.Internals
{
    public class TransactionHandleProcess_Tests
    {
        [Theory]
        [InlineData(false, null)]
        [InlineData(false, "secret")]
        [InlineData(true, null)]
        [InlineData(true, "secret")]
        public async Task Process_death_releases_handle_ownership_without_committing_partial_wal(bool shared, string password)
        {
            using var file = new TempFile();
            Seed(file, password);
            using (var holder = new MvccProcess("handle-hold", file, password, shared ? "shared" : "direct"))
            {
                await holder.Expect("ready");
                await MvccProcess.Run("native-rejected", file, password, "direct");
                await holder.Kill();
            }
            Verify(file, password, new[] { 1 });
            Verify(file, password, new[] { 1 });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public async Task Acknowledged_shared_commit_survives_death_before_session_checkpoint(string password)
        {
            using var file = new TempFile();
            Seed(file, password);
            using (var holder = new MvccProcess("handle-hold", file, password, "shared"))
            {
                await holder.Expect("ready");
                holder.Send("commit");
                await holder.Expect("done");
                Assert.True(new System.IO.FileInfo(FileHelper.GetLogFile(file)).Length > 0,
                    "the acknowledged transaction must still require WAL recovery before session checkpoint");
                await holder.Kill();
            }
            Verify(file, password, new[] { 1, 2 });
            Verify(file, password, new[] { 1, 2 });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public async Task Independent_process_waits_for_shared_handle_then_acquires_before_handle_disposal(string password)
        {
            using var file = new TempFile();
            Seed(file, password);
            using var holder = new MvccProcess("handle-hold", file, password, "shared");
            await holder.Expect("ready");
            using var writer = new MvccProcess("handle-writer", file, password, "shared");
            await writer.Expect("attempting");
            var written = writer.Expect("done");
            await Task.Delay(100);
            Assert.False(written.IsCompleted);
            holder.Send("commit");
            await holder.Expect("done");
            await written;
            await writer.Finish();
            await holder.Finish(release: true);
            Verify(file, password, new[] { 1, 2, 3 });
            Verify(file, password, new[] { 1, 2, 3 });
        }

        private static void Seed(string file, string password)
        {
            using var db = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 42 });
            db.GetCollection("rows").EnsureIndex("value", true);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
        }
        private static void Verify(string file, string password, int[] ids)
        {
            using var db = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
            Assert.Equal(ids, db.GetCollection("rows").FindAll().Select(x => x["_id"].AsInt32).OrderBy(x => x));
            Assert.Equal(ids.Length, db.GetCollection("rows").Find(Query.GTE("value", 42)).Count());
            Assert.NotNull(db.GetCollection("sentinel").FindById(9));
        }
    }
}
#endif
