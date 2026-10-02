using System;
using System.Threading;
using LiteDB.Client.Shared;
using LiteDB.Utils;

namespace LiteDB
{
    public partial class SharedEngine
    {
        // A handle's child closes its operation engine normally; the public session owns
        // the final checkpoint policy. Durable commits remain in the authoritative WAL.
        private bool _transactionChild;
        [TeardownPath("SharedEngine.Dispose", TeardownDisposition.Propagated | TeardownDisposition.Discarded, "Steps propagate; core close list dropped.")]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                this.DisposeConnection();
            }
            finally
            {
                // ReturnTransactionChild uses the same lock and refuses publication after
                // _disposed is set. Both cached and parent admission must drain on errors.
                SharedEngine cached;
                lock (_useLock)
                {
                    cached = _cachedTransactionChild;
                    _cachedTransactionChild = null;
                }
                try { cached?.Dispose(); }
                finally { _settings.SharedAdmission.Dispose(); }
            }
        }

        private void DisposeConnection()
        {
            TeardownSteps.Before("SharedEngine.Dispose.retire-reads"); this.RetireCoordinatedReads(); TeardownSteps.After("SharedEngine.Dispose.retire-reads");
            // Any thread can end a pin; its holder closes the engine and releases. Read
            // under the lock that orders a starting pin's publication with this Dispose.
            SharedMutexPin pin;
            lock (_useLock) pin = _pin;
            this.MarkDisposeOverlaps(pin);
            if (pin != null)
            {
                pin.RequestRelease(force: true);
                if (!pin.CanWaitFrom(Thread.CurrentThread))
                {
                    // The pin's holder still closes its engine; its streams close on return.
                    TeardownSteps.Before("SharedEngine.Dispose.early-handles", _handles != null); _handles?.Dispose(); TeardownSteps.After("SharedEngine.Dispose.early-handles", _handles != null);
                    TeardownSteps.Before("SharedEngine.Dispose.early-readers"); _readers.Dispose(); TeardownSteps.After("SharedEngine.Dispose.early-readers");
                    return;
                }
                TeardownSteps.Before("SharedEngine.Dispose.wait-pin"); pin.WaitReleased(); TeardownSteps.After("SharedEngine.Dispose.wait-pin");
            }

            // Calls admitted before Dispose started finish first; later ones are refused.
            this.WaitForAdmittedCalls();
            var closed = false;
#if DEBUG || TESTING
            // Wait-for graph: the close runs under every hold of this connection.
            using (LiteDB.Utils.WaitGraph.Executing(this, claimsAll: true))
#endif
            lock (_useLock)
            {
                if (_engine != null)
                {
                    var closing = _engine;
                    this.ObservedClose(closing, () => closing.Close(final: true));
                    _engine = null;
                    closed = true;
                }
                this.CloseMutexSnapshotsLocked();
                _databaseUsers = 0;
            }
            // Open readers and transactions of any thread end with the connection.
            _owner.ReleaseAll();
            // Operations left a WAL below the close threshold: checkpoint it now, so
            // the data file alone is the database again once every connection closed.
            if (!closed && !_transactionChild) this.CheckpointOnDispose();
            TeardownSteps.Before("SharedEngine.Dispose.handles", _handles != null); _handles?.Dispose(); TeardownSteps.After("SharedEngine.Dispose.handles", _handles != null);
            // Leased readers may outlive the connection; the slot file closes after the last.
            TeardownSteps.Before("SharedEngine.Dispose.readers"); _readers.Dispose(); TeardownSteps.After("SharedEngine.Dispose.readers");
            // A disposed connection holds no mutex, even for the moment its holder
            // needs to release it; another connection's final close may try it next.
            _owner.WaitForRelease();
            TeardownSteps.Before("SharedEngine.Dispose.coordination"); this.DisposeCoordination(); TeardownSteps.After("SharedEngine.Dispose.coordination");
        }

        /// <summary>
        /// Readers streaming under the mutex end with their ownership, like the
        /// operation engine: a later read would no longer be ordered with writers.
        /// </summary>
        private void CloseMutexSnapshotsLocked()
        {
            foreach (var snapshot in _mutexSnapshots) this.ObservedClose(snapshot, () => snapshot.Close(checkpoint: false));
            _mutexSnapshots.Clear();
        }
    }
}
