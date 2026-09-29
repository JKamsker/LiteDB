using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Client.Direct;
using LiteDB.Client.Shared;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectPool_Tests
    {
        internal static LiteEngine Engine(LiteDatabase db) => ((DirectEngineLease)db.Context.Engine).Engine;
        internal static bool Locked(string path)
        {
            using var probe = new DatabaseFileLock(path, readOnly: true, create: false);
            return probe.Conflicts(DatabaseFileLock.Admission);
        }

        [Fact]
        public void Owners_share_actual_engine_and_only_final_owner_releases_storage()
        {
            using var file = new TempFile();
            using var first = new LiteDatabase(file);
            using var second = new LiteDatabase(file);
            Assert.Same(Engine(first), Engine(second));
            var stale = first.GetCollection("rows");
            stale.EnsureIndex("value", true);
            stale.Insert(new BsonDocument { ["_id"] = 1, ["value"] = 101 });
            second.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 7 });
            first.Dispose();
            first.Dispose();
            Assert.True(Locked(file));
            Assert.Throws<ObjectDisposedException>(() => stale.Count());
            Assert.Throws<ObjectDisposedException>(() => first.Execute("SELECT $ FROM rows"));
            Assert.Throws<ObjectDisposedException>(() => first.BeginTrans());
            Assert.Equal(1, second.GetCollection("rows").FindOne("value = 101")["_id"].AsInt32);
            second.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 102 });
            second.Dispose();
            Assert.False(Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.Equal(2, cold.GetCollection("rows").Count());
            Assert.Equal(1, cold.GetCollection("sentinel").Count());
            Assert.Throws<LiteException>(() => cold.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 101 }));
        }

        [Fact]
        public async Task Concurrent_first_opens_share_engine_and_all_acknowledged_writes_survive()
        {
            using var file = new TempFile();
            using var start = new ManualResetEventSlim();
            var owners = new LiteDatabase[8];
            try
            {
                var tasks = Enumerable.Range(0, owners.Length).Select(i => Task.Run(() =>
                {
                    Assert.True(start.Wait(TimeSpan.FromSeconds(10)));
                    var db = owners[i] = new LiteDatabase(file);
                    db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = i });
                })).ToArray();
                start.Set();
                await Task.WhenAll(tasks);
                Assert.All(owners, owner => Assert.Same(Engine(owners[0]), Engine(owner)));
                Assert.Equal(8, owners[0].GetCollection("rows").Count());
                await Task.WhenAll(owners.Select(owner => Task.Run(() => { owner.Dispose(); owner.Dispose(); })));
                Assert.False(Locked(file));
                using var cold = new LiteDatabase(file);
                Assert.Equal(Enumerable.Range(0, 8), cold.GetCollection("rows").FindAll().Select(x => x["_id"].AsInt32).OrderBy(x => x));
            }
            finally { start.Set(); foreach (var owner in owners) owner?.Dispose(); }
        }

        [Fact]
        public void Reentrant_last_owner_disposal_keeps_operation_and_commit_protected()
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(file);
            var rows = db.GetCollection("rows");
            IEnumerable<BsonDocument> Documents()
            {
                db.Dispose();
                Assert.True(Locked(file));
                yield return new BsonDocument { ["_id"] = 42 };
            }
            Assert.Equal(1, rows.Insert(Documents()));
            Assert.False(Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("rows").FindById(42));
        }

        [Fact]
        public async Task Final_close_reservation_prevents_reopening_during_stream_cleanup()
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(file);
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var closing = new ManualResetEventSlim();
            using var finish = new ManualResetEventSlim();
            using var opening = new ManualResetEventSlim();
            Task close = null;
            Task<LiteDatabase> open = null;
            DirectEnginePool.BeforeFinalClose = path =>
            {
                if (path != file.Filename) return;
                closing.Set();
                Assert.True(finish.Wait(TimeSpan.FromSeconds(10)));
            };
            DirectEnginePool.WaitingForClose = path => { if (path == file.Filename) opening.Set(); };
            try
            {
                close = Task.Run(() => db.Dispose());
                Assert.True(closing.Wait(TimeSpan.FromSeconds(10)));
                open = Task.Run(() => new LiteDatabase(file));
                Assert.True(opening.Wait(TimeSpan.FromSeconds(10)));
                Assert.True(Locked(file));
                Assert.False(open.IsCompleted);
                finish.Set();
                await close;
                using var next = await open;
                Assert.Equal(1, next.GetCollection("rows").Count());
            }
            finally
            {
                finish.Set();
                DirectEnginePool.BeforeFinalClose = null;
                DirectEnginePool.WaitingForClose = null;
                if (close != null) await close;
                if (open != null) (await open).Dispose();
            }
            Assert.False(Locked(file));
        }

        [Fact]
        public void Failed_first_open_does_not_strand_a_pool_reservation()
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(new ConnectionString { Filename = file, Password = "secret" }))
                seed.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            for (var i = 0; i < 2; i++) Assert.Throws<LiteException>(() => new LiteDatabase(file));
            Assert.False(Locked(file));
            using var good = new LiteDatabase(new ConnectionString { Filename = file, Password = "secret" });
            Assert.Equal(1, good.GetCollection("rows").Count());
        }

        [Fact]
        public void Memory_databases_remain_independent()
        {
            using var first = new LiteDatabase(":memory:");
            using var second = new LiteDatabase(":memory:");
            first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            Assert.Equal(0, second.GetCollection("rows").Count());
        }
    }
}
