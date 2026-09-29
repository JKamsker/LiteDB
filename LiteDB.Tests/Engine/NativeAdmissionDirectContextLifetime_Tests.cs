using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectContextLifetime_Tests
    {
        [Fact]
        public async Task Rebuild_waits_for_transactions_in_every_context()
        {
            using var file = new TempFile();
            using var first = new LiteDatabase(file);
            using var second = new LiteDatabase(file);
            first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            first.BeginTrans();
            first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
            using var waiting = new ManualResetEventSlim();
            var engine = NativeAdmissionDirectPool_Tests.Engine(first);
            engine.SimulateBeforeExclusiveAdmission = () => waiting.Set();
            var exclusive = Task.Run(() => second.Rebuild());
            var rolledBack = false;
            try
            {
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(10)));
                Assert.False(exclusive.IsCompleted);
                rolledBack = first.Rollback();
                Assert.True(rolledBack);
            }
            finally
            {
                engine.SimulateBeforeExclusiveAdmission = null;
                if (!rolledBack) first.Rollback();
                await exclusive;
            }
            Assert.Equal(1, first.GetCollection("rows").Count());
            second.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3 });
            Assert.Equal(2, first.GetCollection("rows").Count());
        }

        [Fact]
        public void Checkpoint_from_another_context_preserves_the_live_transactions_wal()
        {
            using var file = new TempFile();
            using (var first = new LiteDatabase(new ConnectionString { Filename = file, TransactionPageLimit = 1 }))
            using (var second = new LiteDatabase(file))
            {
                Seed(first);
                WriteUncommitted(first);
                second.Checkpoint();
                Assert.NotNull(first.GetCollection("rows").FindById(2));
                Assert.Null(second.GetCollection("rows").FindById(2));
                first.Rollback();
                second.Checkpoint();
            }
            Verify(file, committedThird: false);
            Verify(file, committedThird: false);
        }

        [Fact]
        public void Abandoned_explicit_context_is_rolled_back_by_the_next_live_owner_operation()
        {
            using var file = new TempFile();
            using (var retained = new LiteDatabase(file))
            {
                Seed(retained);
                var abandoned = AbandonTransaction(file);
                for (var i = 0; i < 20 && abandoned.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                Assert.False(abandoned.IsAlive);
                retained.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 30 });
                Assert.Null(retained.GetCollection("rows").FindById(2));
                Assert.Equal(2, retained.GetCollection("rows").Count());
                retained.Checkpoint();
            }
            Verify(file, committedThird: true);
            Verify(file, committedThird: true);
        }

        [Fact]
        public async Task Concurrent_siblings_drain_multiple_abandoned_transactions_without_losing_commits()
        {
            using var file = new TempFile();
            using (var first = new LiteDatabase(file))
            using (var second = new LiteDatabase(file))
            {
                Seed(first);
                first.GetCollection("other").Insert(new BsonDocument { ["_id"] = 1 });
                var one = AbandonTransaction(file);
                var two = AbandonTransaction(file, "other");
                for (var i = 0; i < 20 && (one.IsAlive || two.IsAlive); i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                Assert.False(one.IsAlive);
                Assert.False(two.IsAlive);
                using var start = new ManualResetEventSlim();
                var left = Task.Run(() => { start.Wait(); first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 30 }); });
                var right = Task.Run(() => { start.Wait(); second.GetCollection("other").Insert(new BsonDocument { ["_id"] = 3 }); });
                start.Set();
                await Task.WhenAll(left, right);
                Assert.Empty(NativeAdmissionDirectPool_Tests.Engine(first).GetMonitor().Transactions);
                Assert.Equal(new[] { 1, 3 }, second.GetCollection("other").FindAll().Select(row => row["_id"].AsInt32));
                first.Checkpoint();
            }
            Verify(file, committedThird: true);
            Verify(file, committedThird: true);
            using var cold = new LiteDatabase(file);
            Assert.Equal(new[] { 1, 3 }, cold.GetCollection("other").FindAll().Select(row => row["_id"].AsInt32));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Foreign_reader_disposal_preserves_each_contexts_transaction(bool closeOwner)
        {
            using var file = new TempFile();
            using (var first = new LiteDatabase(file))
            using (var second = new LiteDatabase(file))
            {
                Seed(first);
                first.BeginTrans();
                first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 30 });
                using var reader = first.Execute("SELECT $ FROM rows");
                Assert.True(reader.Read());
                if (closeOwner) first.Dispose();
                var cleanup = Task.Run(() =>
                {
                    Assert.True(second.BeginTrans());
                    second.GetCollection("other").Insert(new BsonDocument { ["_id"] = 7 });
                    var monitor = NativeAdmissionDirectPool_Tests.Engine(second).GetMonitor();
                    Assert.Equal(2, monitor.Transactions.Count);
                    reader.Dispose();
                    reader.Dispose();
                    Assert.Equal(closeOwner ? 1 : 2, monitor.Transactions.Count);
                    Assert.Null(second.GetCollection("rows").FindById(3));
                    Assert.NotNull(second.GetCollection("other").FindById(7));
                    Assert.True(second.Commit());
                });
                Assert.True(cleanup.Wait(TimeSpan.FromSeconds(10)));
                // A's completion stays on its actual opening thread; B completed on its worker.
                if (!closeOwner) Assert.True(first.Rollback());
                Assert.Null(second.GetCollection("rows").FindById(3));
                await cleanup;
            }
            for (var i = 0; i < 2; i++)
            {
                Verify(file, committedThird: false);
                using var cold = new LiteDatabase(file);
                Assert.NotNull(cold.GetCollection("other").FindById(7));
            }
        }

        [Fact]
        public async Task Foreign_context_disposal_wakes_a_sibling_already_waiting_for_its_collection()
        {
            using var file = new TempFile();
            using (var first = new LiteDatabase(new ConnectionString { Filename = file, TransactionPageLimit = 1 }))
            using (var second = new LiteDatabase(file))
            {
                Seed(first);
                WriteUncommitted(first);
                var engine = NativeAdmissionDirectPool_Tests.Engine(first);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var locker = (LockService)typeof(LiteEngine).GetField("_locker", flags).GetValue(engine);
                var locks = (System.Collections.Concurrent.ConcurrentDictionary<string, CollectionLock>)
                    typeof(LockService).GetField("_collections", flags).GetValue(locker);
                using var waiting = new ManualResetEventSlim();
                locks["rows"].BeforeWait = () => waiting.Set();
                var writer = Task.Run(() => second.GetCollection("rows").Insert(
                    new BsonDocument { ["_id"] = 3, ["value"] = 30 }));
                try
                {
                    Assert.True(waiting.Wait(TimeSpan.FromSeconds(10)));
                    Assert.False(writer.IsCompleted);
                    await Task.Run(() => first.Dispose());
                    await writer;
                }
                finally
                {
                    locks["rows"].BeforeWait = null;
                    first.Dispose();
                    await writer;
                }
                Assert.Empty(engine.GetMonitor().Transactions);
                second.Checkpoint();
            }
            Verify(file, committedThird: true);
            Verify(file, committedThird: true);
        }

        [Fact]
        public void Failed_context_rollback_stops_all_contexts_and_preserves_committed_state()
        {
            using var file = new TempFile();
            using (var retained = new LiteDatabase(file))
            using (var closing = new LiteDatabase(new ConnectionString { Filename = file, TransactionPageLimit = 1 }))
            {
                Seed(retained);
                WriteUncommitted(closing);
                var engine = NativeAdmissionDirectPool_Tests.Engine(retained);
                var reached = false;
                engine.SimulateDiskWriteFail = page => { reached = true; throw new IOException("context rollback write failure"); };
                Assert.Throws<IOException>(() => closing.Dispose());
                Assert.True(reached);
                Assert.Throws<IOException>(() => retained.GetCollection("rows").Count());
                Assert.Throws<DatabaseAdmissionException>(() => new LiteDatabase(file));
            }
            Verify(file, committedThird: false);
            Verify(file, committedThird: false);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AbandonTransaction(string filename, string collection = "rows")
        {
            var database = new LiteDatabase(new ConnectionString { Filename = filename, TransactionPageLimit = 1 });
            try { WriteUncommitted(database, collection); }
            catch { database.Dispose(); throw; }
            return new WeakReference(database);
        }

        private static void WriteUncommitted(LiteDatabase database, string collection = "rows")
        {
            Assert.True(database.BeginTrans());
            database.GetCollection(collection).Insert(new BsonDocument
            { ["_id"] = 2, ["value"] = 20, ["payload"] = new string('x', 50000) });
            var transaction = NativeAdmissionDirectPool_Tests.Engine(database).GetMonitor().Transactions.Single(item => item.Snapshots.Any(snapshot => snapshot.CollectionName == collection));
            Assert.True(transaction.Pages.NewPages.Count > 0);
            transaction.Safepoint();
            Assert.True(transaction.Pages.DirtyPages.Count > 0, "The explicit transaction must reach unconfirmed WAL writes.");
        }

        private static void Seed(LiteDatabase database)
        {
            database.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
            database.GetCollection("rows").EnsureIndex("value", true);
            database.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
            database.Checkpoint();
        }

        private static void Verify(string filename, bool committedThird)
        {
            using var database = new LiteDatabase(filename);
            Assert.Equal(committedThird ? new[] { 1, 3 } : new[] { 1 },
                database.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32));
            Assert.NotNull(database.GetCollection("rows").FindOne("value = 10"));
            Assert.Null(database.GetCollection("rows").FindOne("value = 20"));
            Assert.Equal(committedThird, database.GetCollection("rows").FindOne("value = 30") != null);
            Assert.NotNull(database.GetCollection("sentinel").FindById(9));
        }
    }
}
