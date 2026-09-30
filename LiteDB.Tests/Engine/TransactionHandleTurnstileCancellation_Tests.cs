using System;
using System.Threading;
using LiteDB.Client.Shared;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleTurnstileCancellation_Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Cancellation_releases_only_the_waiters_turn(bool holdTurn)
        {
            using var mutex = new Mutex(false, "LiteDB-cancel-main-" + Guid.NewGuid());
            using var turn = new Mutex(false, "LiteDB-cancel-turn-" + Guid.NewGuid());
            using var cancellation = new CancellationTokenSource();
            using var starting = new ManualResetEventSlim();
            using var reachedMain = new ManualResetEventSlim();
            using var contended = new ManualResetEventSlim();
            var gate = new SharedMutexTurnstile(turn) { BeforeMainWait = () => reachedMain.Set() };
            var held = holdTurn ? turn : mutex;
            gate.BeforeContendedWait = target => { if (ReferenceEquals(target, held)) contended.Set(); };
            held.WaitOne();
            Exception error = null;
            var waiter = new Thread(() =>
            {
                starting.Set();
                error = Record.Exception(() =>
                {
                    gate.Wait(mutex, cancellation.Token);
                    mutex.ReleaseMutex();
                });
            }) { IsBackground = true };
            waiter.Start();
            try
            {
                Assert.True(starting.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(contended.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal(!holdTurn, reachedMain.IsSet);
                cancellation.Cancel();
                Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
                Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(error).CancellationToken);
                // Cancellation must not release the other thread's ownership.
                Assert.False(AcquireOnAnotherThread(held));
                if (!holdTurn) Assert.True(AcquireOnAnotherThread(turn));
            }
            finally
            {
                cancellation.Cancel();
                held.ReleaseMutex();
                Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            }
            Assert.True(AcquireOnAnotherThread(turn));
            Assert.True(AcquireOnAnotherThread(mutex));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Abandoned_ownership_retains_the_existing_contract(bool abandonTurn)
        {
            using var mutex = new Mutex(false, "LiteDB-abandon-main-" + Guid.NewGuid());
            using var turn = new Mutex(false, "LiteDB-abandon-turn-" + Guid.NewGuid());
            using var cancellation = new CancellationTokenSource();
            var abandoned = abandonTurn ? turn : mutex;
            var owner = new Thread(() => abandoned.WaitOne());
            owner.Start();
            Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
            var gate = new SharedMutexTurnstile(turn);
            var error = Record.Exception(() => gate.Wait(mutex, cancellation.Token));
            try
            {
                if (abandonTurn) Assert.Null(error);
                else Assert.IsType<AbandonedMutexException>(error);
                Assert.False(AcquireOnAnotherThread(mutex));
                Assert.True(AcquireOnAnotherThread(turn));
            }
            finally { mutex.ReleaseMutex(); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Contended_wait_preserves_release_and_abandonment(bool holdTurn, bool abandon)
        {
            using var mutex = new Mutex(false, "LiteDB-wake-main-" + Guid.NewGuid());
            using var turn = new Mutex(false, "LiteDB-wake-turn-" + Guid.NewGuid());
            using var cancellation = new CancellationTokenSource();
            using var ownerReady = new ManualResetEventSlim();
            using var endOwner = new ManualResetEventSlim();
            using var contended = new ManualResetEventSlim();
            using var acquired = new ManualResetEventSlim();
            using var endWaiter = new ManualResetEventSlim();
            var held = holdTurn ? turn : mutex;
            var owner = new Thread(() =>
            {
                held.WaitOne();
                ownerReady.Set();
                endOwner.Wait();
                if (!abandon) held.ReleaseMutex();
            }) { IsBackground = true };
            var gate = new SharedMutexTurnstile(turn);
            gate.BeforeContendedWait = target => { if (ReferenceEquals(target, held)) contended.Set(); };
            Exception error = null;
            var waiter = new Thread(() =>
            {
                error = Record.Exception(() => gate.Wait(mutex, cancellation.Token));
                acquired.Set();
                endWaiter.Wait();
                if (error == null || error is AbandonedMutexException) mutex.ReleaseMutex();
            }) { IsBackground = true };
            owner.Start();
            try
            {
                Assert.True(ownerReady.Wait(TimeSpan.FromSeconds(5)));
                waiter.Start();
                // The zero-wait acquisition failed, so the platform-specific
                // contended path must handle the following release or death.
                Assert.True(contended.Wait(TimeSpan.FromSeconds(5)));
                endOwner.Set();
                Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
                Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
                // A completed wait has retired its cancellation callback/event;
                // later cancellation must not signal a disposed event or release ownership.
                cancellation.Cancel();
                if (abandon && !holdTurn) Assert.IsType<AbandonedMutexException>(error);
                else Assert.Null(error);
                Assert.False(AcquireOnAnotherThread(mutex));
                Assert.True(AcquireOnAnotherThread(turn));
            }
            finally
            {
                endOwner.Set();
                endWaiter.Set();
                Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
                if ((waiter.ThreadState & ThreadState.Unstarted) == 0)
                    Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            }
            Assert.True(AcquireOnAnotherThread(mutex));
        }

        [Fact]
        public void Already_cancelled_wait_never_acquires_an_available_mutex()
        {
            using var mutex = new Mutex(false, "LiteDB-cancel-free-" + Guid.NewGuid());
            using var turn = new Mutex(false, "LiteDB-cancel-free-turn-" + Guid.NewGuid());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => new SharedMutexTurnstile(turn).Wait(mutex, cancellation.Token));
            Assert.True(AcquireOnAnotherThread(mutex));
            Assert.True(AcquireOnAnotherThread(turn));
        }

        private static bool AcquireOnAnotherThread(Mutex mutex)
        {
            var acquired = false;
            var thread = new Thread(() =>
            {
                acquired = mutex.WaitOne(0);
                if (acquired) mutex.ReleaseMutex();
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            return acquired;
        }
    }
}
