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
