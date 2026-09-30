using System;
using System.Runtime.InteropServices;
using System.Threading;
#if DEBUG || TESTING
using LiteDB.Utils;
#endif

namespace LiteDB.Client.Shared
{
    /// <summary>
    /// A second named mutex that every blocking acquisition of the shared mutex passes
    /// while it waits. Named mutexes are not fair: a party that just released the mutex
    /// (an ending pin that restarts, a tight loop of operations) can take it again
    /// before a waiter that was woken gets to run. A waiter holds the turnstile until it
    /// owns the mutex, so the releasing party blocks at the turnstile and the waiter goes
    /// first. The turnstile also tells a holder, in any process, that someone is waiting.
    /// Releases never touch it, so no holder of the shared mutex ever waits for it while
    /// a waiter waits for the holder. Versions without a turnstile still interoperate:
    /// they only compete for the shared mutex as before.
    /// </summary>
    internal sealed class SharedMutexTurnstile
    {
        private readonly Mutex _turn;
#if DEBUG || TESTING
        internal System.Action BeforeMainWait { get; set; }
        internal Action<Mutex> BeforeContendedWait;
#endif

        public SharedMutexTurnstile(Mutex turn)
        {
            _turn = turn;
#if DEBUG || TESTING
            _graphTurn = WaitGraph.Of(turn, "named-mutex", WaitPrimitive.NamedMutex);
#endif
        }

#if DEBUG || TESTING
        // A queued waiter holds the turn until it owns the shared mutex.
        private readonly WaitGraph.Resource _graphTurn;
#endif

        /// <summary>
        /// Block until <paramref name="mutex"/> is owned, queued at the turnstile.
        /// Throws <see cref="AbandonedMutexException"/> as <see cref="WaitHandle.WaitOne()"/> does.
        /// </summary>
        public void Wait(Mutex mutex, CancellationToken closing = default)
        {
#if DEBUG || TESTING
            bool queued;
            var graphBound = closing.CanBeCanceled ? WaitBound.Cancellation : WaitBound.Unbounded;
            using (WaitGraph.Wait(_graphTurn, graphBound, "SharedMutexTurnstile.Wait (turn)"))
                queued = this.Enter(closing);
            if (queued) WaitGraph.Acquired(_graphTurn, site: "SharedMutexTurnstile.Wait (turn)");
#else
            var queued = this.Enter(closing);
#endif
            try
            {
#if DEBUG || TESTING
                this.BeforeMainWait?.Invoke();
                using (WaitGraph.Wait(WaitGraph.Of(mutex, "named-mutex", WaitPrimitive.NamedMutex), graphBound, "SharedMutexTurnstile.Wait"))
#endif
                WaitCancellable(mutex, closing);
            }
            finally
            {
#if DEBUG || TESTING
                if (queued) WaitGraph.Released(_graphTurn);
#endif
                if (queued) _turn.ReleaseMutex();
            }
        }

        /// <summary>Try both gates without barging ahead of an already queued waiter.</summary>
        public bool TryWait(Mutex mutex)
        {
            try { if (!_turn.WaitOne(0)) return false; }
            catch (AbandonedMutexException) { }
            try { return mutex.WaitOne(0); }
            finally { _turn.ReleaseMutex(); }
        }

        /// <summary>True when another participant is queued for the shared mutex.</summary>
        public bool HasWaiter()
        {
            try
            {
                if (!_turn.WaitOne(0)) return true;
            }
            catch (AbandonedMutexException)
            {
                // A waiter died while queued; this thread now owns the turnstile.
            }
            _turn.ReleaseMutex();
            return false;
        }

        private void WaitCancellable(Mutex mutex, CancellationToken closing)
        {
            if (!closing.CanBeCanceled) { mutex.WaitOne(); return; }
            closing.ThrowIfCancellationRequested();
            if (mutex.WaitOne(0)) return;
#if DEBUG || TESTING
            this.BeforeContendedWait?.Invoke(mutex);
#endif
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Keep a contended waiter in the kernel queue until ownership or
                // cancellation. The uncontended path needs no event or array.
                if (WaitHandle.WaitAny(new WaitHandle[] { closing.WaitHandle, mutex }) == 0)
                    throw new OperationCanceledException(closing);
                return;
            }
            // Unix does not support WaitAny containing a named mutex. Each timed
            // wait still wakes immediately on release; this is not a 10 ms sleep.
            // Once acquired, the turnstile stays owned throughout the main wait,
            // preventing cooperating writers from barging ahead of this waiter.
            do { closing.ThrowIfCancellationRequested(); } while (!mutex.WaitOne(10));
        }

        private bool Enter(CancellationToken closing)
        {
            try
            {
                WaitCancellable(_turn, closing);
                return true;
            }
            catch (AbandonedMutexException)
            {
                // It guards no state: a waiter that died while queued only gave up its turn.
                return true;
            }
        }
    }
}
