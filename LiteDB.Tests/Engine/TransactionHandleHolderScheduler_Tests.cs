using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleHolderScheduler_Tests
    {
        private static void Idle(Thread thread) => Assert.True(SpinWait.SpinUntil(
            () => SharedHolderScheduler.IsIdle(thread), TimeSpan.FromSeconds(5)));

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
                if (value == null) using (ExecutionContext.SuppressFlow()) worker = SharedHolderScheduler.Queue(action);
                else worker = SharedHolderScheduler.Queue(action);
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
            var first = SharedHolderScheduler.Queue(block);
            var second = SharedHolderScheduler.Queue(block);
            Thread third = null;
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                third = SharedHolderScheduler.Queue(done.Set);
                Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            }
            finally { release.Set(); }
            foreach (var worker in new[] { first, second, third })
                Assert.True(SpinWait.SpinUntil(() => !worker.IsAlive || SharedHolderScheduler.IsIdle(worker), TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void Completed_callback_graph_is_collectible_while_its_worker_is_still_idle()
        {
            SharedHolderScheduler.IdleWaitOverride = 10000;
            try
            {
                Thread worker = null;
                WeakReference graph = null;
                TransactionHandle_Tests.OnThread(() => graph = SubmitGraph(out worker));
                Idle(worker);
                for (var i = 0; i < 10 && graph.IsAlive; i++)
                { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
                Assert.False(graph.IsAlive);
                Assert.True(SharedHolderScheduler.IsIdle(worker));
            }
            finally { SharedHolderScheduler.IdleWaitOverride = null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference SubmitGraph(out Thread worker)
        {
            var graph = new object();
            var ambient = new AsyncLocal<object> { Value = graph };
            worker = SharedHolderScheduler.Queue(() => { GC.KeepAlive(graph); GC.KeepAlive(ambient.Value); });
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
            SharedHolderScheduler.IdleWaitOverride = 1;
            var calls = 0;
            var original = SharedHolderScheduler.Queue(start.Wait);
            SharedHolderScheduler.BeforeIdleExpiry = thread =>
            { if (thread == original) { expiring.Set(); release.Wait(); } };
            start.Set();
            try
            {
                Assert.True(expiring.Wait(TimeSpan.FromSeconds(5)));
                var queued = Task.Run(() =>
                {
                    attempting.Set();
                    return SharedHolderScheduler.Queue(() => { Interlocked.Increment(ref calls); finished.Set(); });
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
                SharedHolderScheduler.BeforeIdleExpiry = null;
                SharedHolderScheduler.IdleWaitOverride = null;
            }
        }

    }
}
