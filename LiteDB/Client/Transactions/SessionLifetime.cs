using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
#if DEBUG || TESTING
using LiteDB.Utils;
#endif

namespace LiteDB
{
    /// <summary>Orders session admission, transaction cleanup and final facade release.</summary>
    internal sealed class SessionLifetime
    {
        internal static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(10);
        private readonly object _gate = new object();
        private readonly Dictionary<Thread, int> _threads = new Dictionary<Thread, int>();
        private readonly HashSet<LiteTransaction> _transactions = new HashSet<LiteTransaction>();
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly List<Exception> _errors = new List<Exception>();
        private Action _release;
        private Thread _requestThread, _cleanupThread;
        private LiteTransaction[] _pendingRequests;
        private int _active, _reportedErrors;
        private bool _closeRequested, _cleanupStarted, _closed, _requesting;
        internal CancellationToken Closing => _closing.Token;
        internal readonly object DependencyToken = new object();
#if DEBUG || TESTING
        internal TimeSpan? CloseWaitOverride;
        internal Action BeforeCloseDispatch;
        internal int ActiveHandles { get { lock (_gate) return _transactions.Count; } }
        // Proof overlay (PR #133) wait-for graph: Close waits until the session is closed, which needs
        // every session lease to end (held by the thread in the call), the close request to finish (held
        // by the close worker while it runs) and the final release to run (held by the thread running it).
        // Registered idle handles are rolled back by the close worker; busy ones end on a leased thread.
        private readonly WaitGraph.Resource _graphClosed = new WaitGraph.Resource("session-lifetime", null, WaitPrimitive.Condition, ordered: false);
#endif

        internal Lease Enter()
        {
            var thread = Thread.CurrentThread;
            lock (_gate)
            {
                if (_closeRequested)
                {
                    LiteDB.Utils.Reachability.Sometimes("refusal:session-closed");
                    throw new ObjectDisposedException(nameof(LiteDatabase));
                }
                _threads.TryGetValue(thread, out var depth);
                _threads[thread] = depth + 1;
                _active++;
#if DEBUG || TESTING
                WaitGraph.Acquired(_graphClosed, site: "SessionLifetime.Enter (lease)");
#endif
            }
            return new Lease(this, thread);
        }

        internal void Register(LiteTransaction transaction)
        {
            lock (_gate)
            {
                if (_closeRequested)
                {
                    LiteDB.Utils.Reachability.Sometimes("refusal:session-closed");
                    throw new ObjectDisposedException(nameof(LiteDatabase));
                }
                _transactions.Add(transaction);
            }
        }

        internal void Completed(LiteTransaction transaction)
        {
            bool closing;
            lock (_gate) { _transactions.Remove(transaction); closing = _closeRequested; }
            if (closing) TryFinish();
        }

        internal void Report(Exception error) { lock (_gate) _errors.Add(error); }

        private void Exit(Thread thread)
        {
            bool closing;
            lock (_gate)
            {
#if DEBUG || TESTING
                WaitGraph.Released(_graphClosed, thread);
#endif
                if (--_threads[thread] == 0) _threads.Remove(thread);
                _active--;
                closing = _closeRequested;
            }
            if (closing) TryFinish();
        }

        internal void Close(Action release, TimeSpan? wait = null)
        {
            var elapsed = Stopwatch.StartNew();
            lock (_gate)
            {
                if (_threads.ContainsKey(Thread.CurrentThread) || _requestThread == Thread.CurrentThread ||
                    _cleanupThread == Thread.CurrentThread || SessionCloseDependency.Contains(DependencyToken))
                {
                    LiteDB.Utils.Reachability.Sometimes("refusal:session-close-reentrant");
                    throw new InvalidOperationException("Cannot close a session from inside its executing operation.");
                }
                if (!_closeRequested)
                {
                    _closeRequested = true;
                    _requesting = true;
                    _release = release;
                    _pendingRequests = _transactions.ToArray();
                }
                if (_requesting && _requestThread == null)
                {
                    // Dispose may itself run on a saturated thread pool. Cleanup must
                    // progress independently while this caller waits for its deadline.
                    try
                    {
#if DEBUG || TESTING
                        LiteDB.Utils.Reachability.FaultPoint("BeforeCloseDispatch");
                        BeforeCloseDispatch?.Invoke();
#endif
                        _requestThread = SessionCloseScheduler.Queue(RequestClose);
                    }
                    catch
                    {
                        // Keep Closing and all ownership intact. A later Dispose retries
                        // dispatch rather than leaving an unscheduled request forever.
                        _requestThread = null;
                        throw;
                    }
                }
            }
            var timeout = wait ?? CloseWait;
#if DEBUG || TESTING
            timeout = CloseWaitOverride ?? timeout;
#endif
            lock (_gate)
            {
#if DEBUG || TESTING
                using (WaitGraph.Wait(_graphClosed, WaitBound.After(timeout), "SessionLifetime.Close"))
#endif
                while (!_closed)
                {
                    var remaining = timeout - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                        throw new TimeoutException("Session close is still draining active work. New work is refused; cleanup will finish when that work returns. Dispose may be retried to observe completion.");
                    Monitor.Wait(_gate, remaining);
                }
                if (_errors.Count > _reportedErrors)
                {
                    var failure = _errors[_reportedErrors];
                    for (var i = _reportedErrors + 1; i < _errors.Count; i++)
                        failure.Data["LiteDB.SessionCleanup." + i] = _errors[i];
                    _reportedErrors = _errors.Count;
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
        }

        private void RequestClose()
        {
#if DEBUG || TESTING
            WaitGraph.Acquired(_graphClosed, site: "SessionLifetime.RequestClose (close worker)");
#endif
            try
            {
                _closing.Cancel();
                foreach (var transaction in _pendingRequests) transaction.RequestClose();
            }
            catch (Exception error) { Report(error); }
            finally
            {
                lock (_gate) { _requesting = false; _requestThread = null; _pendingRequests = null; }
#if DEBUG || TESTING
                WaitGraph.Released(_graphClosed);
#endif
                TryFinish();
            }
        }

        [LiteDB.Utils.TeardownPath("SessionLifetime.TryFinish", LiteDB.Utils.TeardownDisposition.RecordedAsCleanupError | LiteDB.Utils.TeardownDisposition.Propagated,
            "A release failure is recorded in the session's errors and the waiting Dispose rethrows the first once " +
            "(later ones in Data[LiteDB.SessionCleanup.i]); the session becomes Closed either way " +
            "(SessionLifetime.cs; docs/transaction-handles.md 'reports any deferred cleanup failure once').")]
        private void TryFinish()
        {
            Action release;
            lock (_gate)
            {
                if (!_closeRequested || _requesting || _cleanupStarted || _active != 0 || _transactions.Count != 0) return;
                _cleanupStarted = true;
                _cleanupThread = Thread.CurrentThread;
                release = _release;
#if DEBUG || TESTING
                WaitGraph.Acquired(_graphClosed, site: "SessionLifetime.TryFinish (release)");
#endif
            }
            try
            {
                LiteDB.Utils.TeardownSteps.Before("SessionLifetime.TryFinish.release");
                release();
                LiteDB.Utils.TeardownSteps.After("SessionLifetime.TryFinish.release");
            }
            catch (Exception error) { Report(error); }
            finally
            {
                lock (_gate)
                {
#if DEBUG || TESTING
                    WaitGraph.Released(_graphClosed);
#endif
                    _closed = true; _release = null; _cleanupThread = null; Monitor.PulseAll(_gate);
                }
            }
        }

        internal readonly struct Lease : IDisposable
        {
            private readonly SessionLifetime _owner;
            private readonly Thread _thread;
            internal Lease(SessionLifetime owner, Thread thread) { _owner = owner; _thread = thread; }
            public void Dispose() => _owner?.Exit(_thread);
        }
    }
}
