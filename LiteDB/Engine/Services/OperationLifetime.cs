using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
#if DEBUG || TESTING
using LiteDB.Utils;
#endif

namespace LiteDB.Engine
{
    /// <summary>Retains a service generation through execution and its completion tail.</summary>
    internal sealed class OperationLifetime
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Thread, int> _threads = new Dictionary<Thread, int>();
        private int _active;
        private Thread _exclusive;
        private Action _deferredClose;
#if DEBUG || TESTING
        internal Action WaitingForMaintenance;
        // Proof overlay (PR #133) wait-for graph: operation leases (thread-keyed, released by their
        // thread) and the exclusive (close/rebuild/deferred close) slot, held by the thread that runs it.
        private readonly WaitGraph.Resource _graphLeases = new WaitGraph.Resource("operation-lifetime", "leases", WaitPrimitive.Lease, ordered: false);
        private readonly WaitGraph.Resource _graphExclusive = new WaitGraph.Resource("operation-lifetime", "exclusive", WaitPrimitive.Gate);
#endif

        internal Lease Enter()
        {
            var thread = Thread.CurrentThread;
            lock (_gate)
            {
#if DEBUG || TESTING
                var graphWait = default(WaitGraph.WaitScope);
                var graphWaiting = false;
                try
                {
#endif
                while (_exclusive != null && _exclusive != thread)
                {
#if DEBUG || TESTING
                    WaitingForMaintenance?.Invoke();
                    if (!graphWaiting)
                    {
                        graphWaiting = true;
                        graphWait = WaitGraph.Wait(_graphExclusive, WaitBound.Unbounded, "OperationLifetime.Enter");
                    }
                    else WaitGraph.Recheck();
#endif
                    Monitor.Wait(_gate);
                }
#if DEBUG || TESTING
                }
                finally { graphWait.Dispose(); }
#endif
                _threads.TryGetValue(thread, out var count);
                _threads[thread] = count + 1;
                _active++;
#if DEBUG || TESTING
                WaitGraph.Acquired(_graphLeases, site: "OperationLifetime.Enter");
#endif
            }
            return new Lease(this, thread, false);
        }

        private void Exit(Thread thread, bool exclusive)
        {
            Action close = null;
            lock (_gate)
            {
                if (exclusive)
                {
#if DEBUG || TESTING
                    WaitGraph.Released(_graphExclusive, thread);
#endif
                    _exclusive = null;
                }
                else
                {
#if DEBUG || TESTING
                    WaitGraph.Released(_graphLeases, thread);
#endif
                    _active--;
                    if (--_threads[thread] == 0) _threads.Remove(thread);
                }
                if (_active == 0 && _exclusive == null && _deferredClose != null)
                {
                    close = _deferredClose;
                    _deferredClose = null;
                    _exclusive = thread;
#if DEBUG || TESTING
                    WaitGraph.Acquired(_graphExclusive, site: "OperationLifetime.Exit (deferred close)");
#endif
                }
                if (_active == 0 || exclusive) Monitor.PulseAll(_gate);
            }
            FinishClose(close);
        }

        private void FinishClose(Action close)
        {
            if (close == null) return;
            try { close(); }
            finally
            {
                lock (_gate)
                {
#if DEBUG || TESTING
                    WaitGraph.Released(_graphExclusive, Thread.CurrentThread);
#endif
                    _exclusive = null; Monitor.PulseAll(_gate);
                }
            }
        }

        internal Lease Exclusive(Func<bool> dependenciesDrained, TimeSpan? timeout = null)
        {
            var thread = Thread.CurrentThread;
            var elapsed = Stopwatch.StartNew();
            lock (_gate)
            {
                if (_exclusive == thread) return default;
                if (_threads.ContainsKey(thread))
                    throw new InvalidOperationException("Cannot close or rebuild from inside an executing engine operation.");
#if DEBUG || TESTING
                var graphWait = default(WaitGraph.WaitScope);
                var graphWaiting = false;
                try
                {
#endif
                while (_exclusive != null || _active != 0 || !dependenciesDrained())
                {
                    if (timeout.HasValue && elapsed.Elapsed >= timeout.Value)
                        throw LiteException.LockTimeout("operation/maintenance", timeout.Value);
#if DEBUG || TESTING
                    // A poll for the active leases and the exclusive slot; bounded only when a timeout is given.
                    // The dependenciesDrained() predicate is not modelled (a miss, never an invented edge).
                    if (!graphWaiting)
                    {
                        graphWaiting = true;
                        graphWait = WaitGraph.Wait(_graphLeases, timeout.HasValue ? WaitBound.After(timeout.Value) : WaitBound.Unbounded,
                            "OperationLifetime.Exclusive", also: _graphExclusive);
                    }
                    else WaitGraph.Recheck();
#endif
                    Monitor.Wait(_gate, 10);
                }
#if DEBUG || TESTING
                }
                finally { graphWait.Dispose(); }
#endif
                _exclusive = thread;
#if DEBUG || TESTING
                WaitGraph.Acquired(_graphExclusive, site: "OperationLifetime.Exclusive");
#endif
            }
            return new Lease(this, thread, true);
        }

        internal void Stop(Action close)
        {
            lock (_gate)
            {
                if (_active != 0 || _exclusive != null) { _deferredClose = close; return; }
                _exclusive = Thread.CurrentThread;
#if DEBUG || TESTING
                WaitGraph.Acquired(_graphExclusive, site: "OperationLifetime.Stop");
#endif
            }
            FinishClose(close);
        }

        internal readonly struct Lease : IDisposable
        {
            private readonly OperationLifetime _owner;
            private readonly Thread _thread;
            private readonly bool _exclusive;
            internal Lease(OperationLifetime owner, Thread thread, bool exclusive)
            { _owner = owner; _thread = thread; _exclusive = exclusive; }
            public void Dispose() => _owner?.Exit(_thread, _exclusive);
        }
    }
}
