using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleCleanupScheduler_Tests
    {
        private static void Idle(Thread thread) => Assert.True(SpinWait.SpinUntil(
            () => SessionCloseScheduler.IsIdle(thread), TimeSpan.FromSeconds(5)));

        [Fact]
        public void Reused_worker_restores_caller_and_suppressed_contexts()
        {
            var ambient = new AsyncLocal<string>();
            Exception error = null;
            Thread previous = null;
            foreach (var value in new[] { "first", "second", null, null })
            {
                ambient.Value = value ?? "must not flow";
                Action action = () =>
                {
                    try { Assert.Equal(value, ambient.Value); ambient.Value = "worker mutation"; }
                    catch (Exception failure) { error = failure; }
                };
                Thread worker;
                if (value == null) using (ExecutionContext.SuppressFlow()) worker = SessionCloseScheduler.Queue(action);
                else worker = SessionCloseScheduler.Queue(action);
                Idle(worker);
                if (error != null) throw error;
                if (previous != null) Assert.Same(previous, worker);
                Assert.Equal(value ?? "must not flow", ambient.Value);
                previous = worker;
            }
            ambient.Value = null;
        }

        [Fact]
        public void Busy_cleanup_workers_do_not_limit_another_session()
        {
            using var entered = new CountdownEvent(2);
            using var release = new ManualResetEventSlim();
            using var done = new ManualResetEventSlim();
            Action block = () => { entered.Signal(); release.Wait(); };
            var first = SessionCloseScheduler.Queue(block);
            var second = SessionCloseScheduler.Queue(block);
            Thread third = null;
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                third = SessionCloseScheduler.Queue(done.Set);
                Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            }
            finally { release.Set(); }
            foreach (var worker in new[] { first, second, third })
                Assert.True(SpinWait.SpinUntil(() => !worker.IsAlive || SessionCloseScheduler.IsIdle(worker), TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void Completed_callback_graph_is_collectible_while_its_worker_is_still_idle()
        {
            SessionCloseScheduler.IdleWaitOverride = 10000;
            try
            {
                Thread worker = null;
                WeakReference graph = null;
                TransactionHandle_Tests.OnThread(() => graph = SubmitGraph(out worker));
                Idle(worker);
                for (var i = 0; i < 10 && graph.IsAlive; i++)
                { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
                Assert.False(graph.IsAlive);
                Assert.True(SessionCloseScheduler.IsIdle(worker));
            }
            finally { SessionCloseScheduler.IdleWaitOverride = null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference SubmitGraph(out Thread worker)
        {
            var graph = new object();
            var ambient = new AsyncLocal<object> { Value = graph };
            worker = SessionCloseScheduler.Queue(() => { GC.KeepAlive(graph); GC.KeepAlive(ambient.Value); });
            return new WeakReference(graph);
        }

        [Fact]
        public async Task Dispatch_after_expiry_removal_runs_once_on_another_worker()
        {
            using var expiring = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var attempting = new ManualResetEventSlim();
            using var finished = new ManualResetEventSlim();
            using var start = new ManualResetEventSlim();
            SessionCloseScheduler.IdleWaitOverride = 1;
            var calls = 0;
            var original = SessionCloseScheduler.Queue(start.Wait);
            SessionCloseScheduler.BeforeIdleExpiry = thread =>
            { if (thread == original) { expiring.Set(); release.Wait(); } };
            start.Set();
            try
            {
                Assert.True(expiring.Wait(TimeSpan.FromSeconds(5)));
                var queued = Task.Run(() =>
                {
                    attempting.Set();
                    return SessionCloseScheduler.Queue(() => { Interlocked.Increment(ref calls); finished.Set(); });
                });
                Assert.True(attempting.Wait(TimeSpan.FromSeconds(5)));
                release.Set();
                var replacement = await queued;
                Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
                Assert.NotSame(original, replacement);
                Assert.Equal(1, calls);
            }
            finally
            {
                release.Set();
                SessionCloseScheduler.BeforeIdleExpiry = null;
                SessionCloseScheduler.IdleWaitOverride = null;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reused_cleanup_preserves_committed_data_and_rolls_back_each_idle_handle(bool shared)
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(file))
            {
                seed.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
                seed.GetCollection("rows").EnsureIndex("value");
            }
            Thread previous = null;
            for (var i = 0; i < 3; i++)
            {
                using var db = new LiteDatabase(new ConnectionString { Filename = file,
                    Connection = shared ? ConnectionType.Shared : ConnectionType.Direct });
                using var tx = db.BeginTransaction();
                tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = i, ["value"] = i });
                Thread worker = null;
                var monitor = ((LiteTransaction)tx).Storage.GetMonitor();
                monitor.AfterTransactionExit = () => { monitor.AfterTransactionExit = null; worker = Thread.CurrentThread; };
                db.Dispose();
                Idle(worker);
                if (previous != null) Assert.Same(previous, worker);
                previous = worker;
                Assert.Equal(LiteTransactionState.RolledBack, tx.State);
            }
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("sentinel").FindById(1));
            Assert.Empty(cold.GetCollection("rows").FindAll());
            Assert.Empty(cold.GetCollection("rows").Find(Query.GTE("value", 0)));
        }
    }
}
