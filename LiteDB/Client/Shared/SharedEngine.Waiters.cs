using System.Threading;

namespace LiteDB
{
    public partial class SharedEngine
    {
        // Threads of this instance blocked on the mutex. A pin ends for them and no new
        // pin starts: a one-shot release request is lost when the owner re-pins first.
        private int _mutexWaiters;
        private readonly object _waitersLock = new object();
#if NET8_0_OR_GREATER
        private long _writerRequest;
#endif

        /// <summary>
        /// Enter the connection's mutex ownership, counted as a waiter meanwhile so that
        /// any pin of this instance, including one started after this call, ends for it.
        /// </summary>
        private bool EnterOwner(bool scoped = false, bool writing = false, CancellationToken closing = default, TransactionAdmission admission = null)
        {
            if (_owner.IsOwnedByCurrentThread) return _owner.Enter(scoped);
#if NET8_0_OR_GREATER
            long request = 0;
            if (writing)
                lock (_snapshotGate) request = this.RequestWriterPressure();
#endif
            this.AddMutexWaiter();
            try
            {
                bool abandoned;
                if (admission != null) abandoned = admission.EnterNative(_owner, scoped);
                else if (!closing.CanBeCanceled) abandoned = _owner.Enter(scoped, SessionCallContext.Closing);
                else
                {
                    closing.ThrowIfCancellationRequested();
#if DEBUG || TESTING
                    // Proof overlay (PR #133): a cancellable poll for ownership and the OS mutex.
                    using (LiteDB.Utils.WaitGraph.Wait(_owner.GraphOwnership, LiteDB.Utils.WaitBound.Cancellation,
                        "SharedEngine.EnterOwner (closing poll)", this, also: _owner.GraphMutexResource))
#endif
                    while (!_owner.TryEnter(out abandoned, scoped))
                    {
#if DEBUG || TESTING
                        LiteDB.Utils.WaitGraph.Recheck();
#endif
                        closing.WaitHandle.WaitOne(10);
                        closing.ThrowIfCancellationRequested();
                    }
                }
#if NET8_0_OR_GREATER
                // A queued writer must not replace the current owner's local token.
                if (writing) Interlocked.Exchange(ref _writerRequest, request);
#endif
                return abandoned;
            }
            catch
            {
#if NET8_0_OR_GREATER
                _coordination?.EndWriterTurn(request);
#endif
                throw;
            }
            finally
            {
                this.RemoveMutexWaiter();
            }
        }

        private void StartWriterPressure()
        {
#if NET8_0_OR_GREATER
            // Called only under ownership, including nested writes after a read-only
            // BeginTrans. Page disposal serializes internally; avoid snapshotGate
            // here because engine opening can already hold useLock.
            if (Interlocked.Read(ref _writerRequest) == 0)
                Interlocked.Exchange(ref _writerRequest,
                    this.RequestWriterPressure());
#endif
        }

        private void EndWriterPressure()
        {
#if NET8_0_OR_GREATER
            // Page disposal serializes with this call. Do not take snapshotGate
            // while closing an engine under useLock (admission takes them in reverse).
            _coordination?.EndWriterTurn(Interlocked.Exchange(ref _writerRequest, 0));
#endif
        }

        private bool HasMutexWaiters() => Volatile.Read(ref _mutexWaiters) > 0;

        private void AddMutexWaiter()
        {
            lock (_waitersLock) _mutexWaiters++;
#if DEBUG || TESTING
            LiteDB.Utils.WaitGraph.Acquired(_graphMutexWaiters, site: "SharedEngine.AddMutexWaiter");
#endif
        }

        private void RemoveMutexWaiter()
        {
#if DEBUG || TESTING
            LiteDB.Utils.WaitGraph.Released(_graphMutexWaiters);
#endif
            lock (_waitersLock)
            {
                if (--_mutexWaiters == 0) Monitor.PulseAll(_waitersLock);
            }
        }

        /// <summary>Block until no thread of this instance waits for the mutex.</summary>
        private void WaitForMutexWaiters()
        {
            lock (_waitersLock)
            {
#if DEBUG || TESTING
                // Proof overlay (PR #133): the poll ends when a session close cancels it.
                using (LiteDB.Utils.WaitGraph.Wait(_graphMutexWaiters,
                    SessionCallContext.Closing.CanBeCanceled ? LiteDB.Utils.WaitBound.Cancellation : LiteDB.Utils.WaitBound.Unbounded,
                    "SharedEngine.WaitForMutexWaiters", this))
#endif
                while (_mutexWaiters > 0)
                {
                    SessionCallContext.Closing.ThrowIfCancellationRequested();
                    Monitor.Wait(_waitersLock, 10);
                }
            }
        }
    }
}
