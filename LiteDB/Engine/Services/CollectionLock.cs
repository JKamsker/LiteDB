using System;
using System.Threading;
#if DEBUG || TESTING
using LiteDB.Utils;
#endif

namespace LiteDB.Engine
{
    /// <summary>
    /// A collection writer belongs to a transaction, with recursive snapshot acquisition.
    /// </summary>
    internal sealed class CollectionLock
    {
        private readonly object _lock = new object();
        private object _owner;
        private Thread _thread;
        private int _depth;
        private bool _stopping;
#if DEBUG || TESTING
        internal Action BeforeWait;
        /// <summary>Wait-for graph resource; the lock service names it after its collection.</summary>
        // Proof overlay (PR #133): the lock is keyed by an owner object (the transaction's pages) and is
        // recursive for that owner only, never for a thread: a waiter here is always another owner, so
        // the primitive is a non-recursive condition (Monitor.Wait on "no other owner").
        internal readonly WaitGraph.Resource Graph = new WaitGraph.Resource("collection-lock", null, WaitPrimitive.Condition);
        private object _graphOwner;
#endif

        public bool TryEnter(object owner, TimeSpan timeout)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_lock)
            {
                if (_stopping) throw LiteException.EngineDisposed();
#if DEBUG || TESTING
                var graphWait = default(WaitGraph.WaitScope);
                var graphWaiting = false;
                try
                {
#endif
                while (_owner != null && !ReferenceEquals(_owner, owner))
                {
                    // Waiting for another session on this thread cannot make progress.
                    if (ReferenceEquals(_thread, Thread.CurrentThread)) return false;
                    var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency;
                    var remaining = timeout - TimeSpan.FromSeconds(elapsed);
                    if (remaining <= TimeSpan.Zero) return false;
#if DEBUG || TESTING
                    BeforeWait?.Invoke();
                    if (!graphWaiting)
                    {
                        graphWaiting = true;
                        graphWait = WaitGraph.Wait(this.Graph, WaitBound.After(timeout), "CollectionLock.TryEnter", owner);
                    }
                    else WaitGraph.Recheck();
#endif
                    if (!Monitor.Wait(_lock, remaining)) return false;
                    if (_stopping) throw LiteException.EngineDisposed();
                }
                _owner = owner;
                _thread = TransactionContext.AdmissionOwner as Thread;
                _depth++;
#if DEBUG || TESTING
                if (_depth == 1) this.GraphAcquired(owner);
#endif
                return true;
#if DEBUG || TESTING
                }
                finally
                {
                    graphWait.Dispose();
                }
#endif
            }
        }

        internal void StopWaiters()
        {
            lock (_lock)
            {
                _stopping = true;
                Monitor.PulseAll(_lock);
            }
        }

        public void Exit(object owner)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_owner, owner)) throw new SynchronizationLockException("Collection lock belongs to another transaction.");
                if (--_depth != 0) return;
#if DEBUG || TESTING
                WaitGraph.Released(this.Graph, _graphOwner, all: true);
                _graphOwner = null;
#endif
                _owner = null;
                _thread = null;
                Monitor.PulseAll(_lock);
            }
        }
#if DEBUG || TESTING

        /// <summary>
        /// A thread-owned transaction (legacy or auto) progresses only on its thread: thread-affine hold.
        /// An explicit transaction executes on whichever thread runs its handle call: a hold of its
        /// context, executed by the frames <see cref="TransactionContext.Enter"/> opens.
        /// </summary>
        private void GraphAcquired(object owner)
        {
            var context = (owner as TransactionPages)?.GraphOwner;
            _graphOwner = context ?? owner;
            WaitGraph.Acquired(this.Graph, _graphOwner, threadAffine: context == null, site: "CollectionLock.TryEnter");
        }
#endif
    }
}
