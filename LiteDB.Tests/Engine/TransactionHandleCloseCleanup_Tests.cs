using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleCloseCleanup_Tests
    {
        [Fact]
        public async Task Concurrent_retries_dispatch_and_release_exactly_once()
        {
            var lifetime = new SessionLifetime();
            var released = 0;
            using var entered = new ManualResetEventSlim();
            using var finish = new ManualResetEventSlim();
            Action release = () =>
            {
                Interlocked.Increment(ref released);
                entered.Set();
                Assert.True(finish.Wait(TimeSpan.FromSeconds(10)));
            };
            lifetime.BeforeCloseDispatch = () => throw new InvalidOperationException("dispatch failed");
            Assert.Throws<InvalidOperationException>(() => lifetime.Close(release));
            lifetime.BeforeCloseDispatch = null;
            var first = Task.Run(() => lifetime.Close(release));
            var second = Task.Run(() => lifetime.Close(release));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal(1, Volatile.Read(ref released));
            }
            finally { finish.Set(); }
            await Task.WhenAll(first, second);
            Assert.Equal(1, released);
        }

        [Fact]
        public void Idle_final_release_is_bounded_and_does_not_block_other_sessions()
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(file);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
            var lifetime = new SessionLifetime();
            using var reached = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            Action release = () =>
            {
                reached.Set();
                Assert.True(resume.Wait(TimeSpan.FromSeconds(10)));
                db.Dispose();
            };
            try
            {
                Assert.Throws<TimeoutException>(() => lifetime.Close(release, TimeSpan.FromMilliseconds(100)));
                Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
                Assert.Throws<ObjectDisposedException>(() => lifetime.Enter());
                Assert.True(NativeAdmissionDirectPool_Tests.Locked(file));
                using var other = new LiteDatabase(":memory:");
                other.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
                other.Dispose();
            }
            finally { resume.Set(); }
            lifetime.Close(release);
            Assert.False(NativeAdmissionDirectPool_Tests.Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("sentinel").FindById(1));
        }

        [Fact]
        public void Failed_cleanup_dispatch_retains_ownership_and_retry_completes()
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(file);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
            using var tx = db.BeginTransaction();
            tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
            var lifetime = (SessionLifetime)typeof(LiteDatabase).GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(db);
            var failure = new InvalidOperationException("cannot start cleanup worker");
            lifetime.BeforeCloseDispatch = () => throw failure;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(db.Dispose));
            Assert.True(NativeAdmissionDirectPool_Tests.Locked(file));
            Assert.Throws<ObjectDisposedException>(() => db.BeginTransaction());
            lifetime.BeforeCloseDispatch = null;
            db.Dispose();
            Assert.Equal(LiteTransactionState.RolledBack, tx.State);
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("sentinel").FindById(1));
            Assert.Null(cold.GetCollection("rows").FindById(2));
        }

        [Fact]
        public async Task Close_deadline_also_covers_idle_rollback_and_cleanup_reentry_is_rejected()
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(file);
            var lifetime = (SessionLifetime)typeof(LiteDatabase).GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(db);
            lifetime.CloseWaitOverride = TimeSpan.FromMilliseconds(100);
            using var tx = db.BeginTransaction();
            tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var reached = new ManualResetEventSlim();
            using var finish = new ManualResetEventSlim();
            var monitor = NativeAdmissionDirectPool_Tests.Engine(db).GetMonitor();
            monitor.AfterTransactionExit = () =>
            {
                monitor.AfterTransactionExit = null;
                Assert.Throws<InvalidOperationException>(db.Dispose);
                reached.Set();
                Assert.True(finish.Wait(TimeSpan.FromSeconds(10)));
            };
            var close = Task.Run(() => Assert.Throws<TimeoutException>(db.Dispose));
            try
            {
                Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
                await close;
                Assert.True(NativeAdmissionDirectPool_Tests.Locked(file));
            }
            finally { finish.Set(); }
            lifetime.CloseWaitOverride = TimeSpan.FromSeconds(10);
            db.Dispose();
            Assert.Equal(LiteTransactionState.RolledBack, tx.State);
            Assert.False(NativeAdmissionDirectPool_Tests.Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.Equal(0, cold.GetCollection("rows").Count());
        }

        [Fact]
        public void Deferred_fatal_close_preserves_outer_maintenance_ownership()
        {
            var lifetime = new LiteDB.Engine.OperationLifetime();
            using var entered = new ManualResetEventSlim();
            using var complete = new ManualResetEventSlim();
            var closed = false;
            var exclusive = lifetime.Exclusive(() => true);
            using (lifetime.Enter()) lifetime.Stop(() => closed = true);
            var worker = new Thread(() => { entered.Set(); using (lifetime.Enter()) complete.Set(); });
            worker.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(complete.Wait(TimeSpan.FromMilliseconds(100)));
            Assert.False(closed);
            exclusive.Dispose();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.True(closed);
            Assert.True(complete.IsSet);
        }
    }
}
