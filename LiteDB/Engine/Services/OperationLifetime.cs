using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

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
#endif

        internal Lease Enter()
        {
            var thread = Thread.CurrentThread;
            lock (_gate)
            {
                while (_exclusive != null && _exclusive != thread)
                {
#if DEBUG || TESTING
                    WaitingForMaintenance?.Invoke();
#endif
                    Monitor.Wait(_gate);
                }
                _threads.TryGetValue(thread, out var count);
                _threads[thread] = count + 1;
                _active++;
            }
            return new Lease(this, thread, false);
        }

        private void Exit(Thread thread, bool exclusive)
        {
            Action close = null;
            lock (_gate)
            {
                if (exclusive) _exclusive = null;
                else
                {
                    _active--;
                    if (--_threads[thread] == 0) _threads.Remove(thread);
                }
                if (_active == 0 && _exclusive == null && _deferredClose != null)
                {
                    close = _deferredClose;
                    _deferredClose = null;
                    _exclusive = thread;
                }
                if (_active == 0 || exclusive) Monitor.PulseAll(_gate);
            }
            FinishClose(close);
        }

        private void FinishClose(Action close)
        {
            if (close == null) return;
            try { close(); }
            finally { lock (_gate) { _exclusive = null; Monitor.PulseAll(_gate); } }
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
                while (_exclusive != null || _active != 0 || !dependenciesDrained())
                {
                    if (timeout.HasValue && elapsed.Elapsed >= timeout.Value)
                        throw LiteException.LockTimeout("operation/maintenance", timeout.Value);
                    Monitor.Wait(_gate, 10);
                }
                _exclusive = thread;
            }
            return new Lease(this, thread, true);
        }

        internal void Stop(Action close)
        {
            lock (_gate)
            {
                if (_active != 0 || _exclusive != null) { _deferredClose = close; return; }
                _exclusive = Thread.CurrentThread;
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
