using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleMaintenanceProgress_Tests
    {
        private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        private static OperationLifetime Operations(LiteEngine engine) =>
            (OperationLifetime)typeof(LiteEngine).GetField("_operations", Fields).GetValue(engine);
        private static void Pending(OperationLifetime operations) => Assert.True(SpinWait.SpinUntil(() =>
            (int)typeof(OperationLifetime).GetField("_waitingExclusive", Fields).GetValue(operations) != 0,
            TimeSpan.FromSeconds(5)), "Maintenance must register before the competing operation starts.");

        [Fact]
        public async Task Waiting_maintenance_holds_new_operations_but_allows_nested_completion()
        {
            var operations = new OperationLifetime();
            var active = operations.Enter();
            using var heldBack = new ManualResetEventSlim();
            using var entered = new ManualResetEventSlim();
            using var finish = new ManualResetEventSlim();
            operations.WaitingForMaintenance = heldBack.Set;
            var maintenance = Task.Run(() =>
            {
                using (operations.Exclusive(() => true, TimeSpan.FromSeconds(10)))
                { entered.Set(); Assert.True(finish.Wait(TimeSpan.FromSeconds(10))); }
            });
            Pending(operations);
            var late = Task.Run(() => { using (operations.Enter()) { } });
            try
            {
                Assert.True(heldBack.Wait(TimeSpan.FromSeconds(5)));
                using (operations.Enter()) { } // nested work must be able to finish
                active.Dispose();
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(late.IsCompleted);
            }
            finally { finish.Set(); }
            await Task.WhenAll(maintenance, late);
        }

        [Fact]
        public async Task Close_waiter_does_not_interrupt_the_current_maintenance_generation()
        {
            var operations = new OperationLifetime();
            using var interrupted = new ManualResetEventSlim();
            var rebuilding = operations.Exclusive(() => true);
            var close = Task.Run(() =>
            { using (operations.Exclusive(() => true, stopWaiters: interrupted.Set)) { } });
            try
            {
                Pending(operations);
                // Acquiring the gate observes the waiter after it has run its admission
                // code and released the gate to wait for the rebuilding owner.
                lock (typeof(OperationLifetime).GetField("_gate", Fields).GetValue(operations))
                    Assert.False(interrupted.IsSet);
            }
            finally { rebuilding.Dispose(); }
            await close;
            Assert.True(interrupted.IsSet);
        }

        [Fact]
        public async Task Maintenance_timeout_removes_pending_admission_and_reader_can_retry()
        {
            var operations = new OperationLifetime();
            using var heldBack = new ManualResetEventSlim();
            operations.WaitingForMaintenance = heldBack.Set;
            var maintenance = Task.Run(() => Assert.Throws<LiteException>(() =>
            { using (operations.Exclusive(() => false, TimeSpan.FromMilliseconds(500))) { } }));
            Pending(operations);
            var reader = Task.Run(() => { using (operations.Enter()) { } });
            Assert.True(heldBack.Wait(TimeSpan.FromSeconds(5)));
            await maintenance;
            Assert.True(reader.Wait(TimeSpan.FromSeconds(5)));
            using (operations.Exclusive(() => true, TimeSpan.FromSeconds(1))) { }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Rebuild_drains_existing_transaction_or_foreign_cursor_before_new_work(bool cursor)
        {
            using var file = new TempFile();
            using (var engine = new LiteEngine(new EngineSettings { Filename = file }))
            using (var db = new LiteDatabase(engine, disposeOnClose: false))
            {
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
                db.GetCollection("rows").EnsureIndex("value");
                IBsonDataReader reader = null;
                if (cursor) reader = engine.Query("rows", new Query { Select = BsonExpression.Create("$") });
                else
                {
                    Assert.True(engine.BeginTrans());
                    db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 20 });
                }
                var maintenance = Task.Run(() => engine.Rebuild());
                Pending(Operations(engine));
                using var heldBack = new ManualResetEventSlim();
                Operations(engine).WaitingForMaintenance = heldBack.Set;
                var late = Task.Run(() => db.GetCollection("rows").Count());
                Assert.True(heldBack.Wait(TimeSpan.FromSeconds(5)));
                if (cursor) await Task.Run(reader.Dispose);
                else Assert.True(engine.Commit());
                Assert.True(maintenance.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(cursor ? 1 : 2, await late);
                Assert.Equal(1, db.GetCollection("rows").Count(Query.EQ("value", 10)));
            }
            using var cold = new LiteDatabase(file);
            Assert.Equal(cursor ? 1 : 2, cold.GetCollection("rows").Count());
            Assert.Equal(1, cold.GetCollection("rows").Count(Query.EQ("value", 10)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Maintenance_progresses_under_sustained_ordinary_reads(bool close)
        {
            using var file = new TempFile();
            using var started = new CountdownEvent(8);
            using var stop = new ManualResetEventSlim();
            using var engine = new LiteEngine(new EngineSettings
            {
                Filename = file,
                ReadTransform = (collection, value) => { Thread.Sleep(10); return value; }
            });
            using var db = new LiteDatabase(engine, disposeOnClose: false);
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            db.Timeout = TimeSpan.FromSeconds(3);
            var errors = new ConcurrentQueue<Exception>();
            var threads = new Thread[8];
            for (var i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Assert.NotNull(db.GetCollection("rows").FindById(1));
                        started.Signal();
                        while (!stop.IsSet) Assert.NotNull(db.GetCollection("rows").FindById(1));
                    }
                    catch (LiteException ex) when (close && ex.ErrorCode == LiteException.ENGINE_DISPOSED) { }
                    catch (Exception ex) { errors.Enqueue(ex); }
                }) { IsBackground = true };
                threads[i].Start();
            }
            try
            {
                Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
                var maintenance = Task.Run(() => { if (close) engine.Dispose(); else engine.Rebuild(); });
                Assert.True(maintenance.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                stop.Set();
                foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            }
            Assert.Empty(errors);
            engine.Dispose();
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("rows").FindById(1));
        }

        [Fact]
        public async Task Raw_close_interrupts_collection_waiter_and_cold_reopen_keeps_only_committed_rows()
        {
            using var file = new TempFile();
            using var engine = new LiteEngine(new EngineSettings { Filename = file });
            using var db = new LiteDatabase(engine, disposeOnClose: false);
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
            db.GetCollection("rows").EnsureIndex("value");
            db.GetCollection("untouched").Insert(new BsonDocument { ["_id"] = 42 });
            Assert.True(engine.BeginTrans());
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 20 });
            var locker = typeof(LiteEngine).GetField("_locker", Fields).GetValue(engine);
            var collections = (ConcurrentDictionary<string, CollectionLock>)typeof(LockService)
                .GetField("_collections", Fields).GetValue(locker);
            using var waiting = new ManualResetEventSlim();
            collections["rows"].BeforeWait = waiting.Set;
            var peer = Task.Run(() => Record.Exception(() =>
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = 30 })));
            Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
            var close = Task.Run(engine.Dispose);
            Assert.True(close.Wait(TimeSpan.FromSeconds(5)), "Close must not wait for the default 60-second collection timeout.");
            Assert.IsType<LiteException>(await peer);
            using var cold = new LiteDatabase(file);
            Assert.Equal(1, cold.GetCollection("rows").Count());
            Assert.NotNull(cold.GetCollection("rows").FindById(1));
            Assert.Null(cold.GetCollection("rows").FindById(2));
            Assert.Null(cold.GetCollection("rows").FindById(3));
            Assert.Equal(1, cold.GetCollection("rows").Count(Query.EQ("value", 10)));
            Assert.NotNull(cold.GetCollection("untouched").FindById(42));
        }
    }
}
