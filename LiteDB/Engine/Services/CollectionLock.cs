using System;
using System.Threading;

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
#if DEBUG || TESTING
        internal Action BeforeWait;
#endif

        public bool TryEnter(object owner, TimeSpan timeout)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_lock)
            {
                while (_owner != null && !ReferenceEquals(_owner, owner))
                {
                    // Waiting for another session on this thread cannot make progress.
                    if (ReferenceEquals(_thread, Thread.CurrentThread)) return false;
                    var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency;
                    var remaining = timeout - TimeSpan.FromSeconds(elapsed);
                    if (remaining <= TimeSpan.Zero) return false;
#if DEBUG || TESTING
                    BeforeWait?.Invoke();
#endif
                    if (!Monitor.Wait(_lock, remaining)) return false;
                }
                _owner = owner;
                _thread = Thread.CurrentThread;
                _depth++;
                return true;
            }
        }

        public void Exit(object owner)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_owner, owner)) throw new SynchronizationLockException("Collection lock belongs to another transaction.");
                if (--_depth != 0) return;
                _owner = null;
                _thread = null;
                Monitor.PulseAll(_lock);
            }
        }
    }
}
