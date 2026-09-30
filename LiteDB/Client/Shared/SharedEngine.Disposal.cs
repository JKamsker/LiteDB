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
            if (!disposing || Volatile.Read(ref _disposed) != 0) return;
            // A callback cannot drain its own call or release that call's writer
            // ownership. Refuse before changing the connection's lifetime state.
            // Reader.Read can execute after SharedEngine.Query admission has ended.
            if (this.AdmittedDepth() != 0 || _engine?.IsExecutingOnCurrentThread == true)
                throw new InvalidOperationException("Cannot close a shared connection from inside its executing operation.");
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            var cleanup = new TryCatch();
            cleanup.Catch(() => this.DisposeConnection(cleanup));
            // A failed pin/core close must not skip unrelated ownership retirement.
            // ReturnTransactionChild refuses publication after _disposed is set.
            SharedEngine cached;
            lock (_useLock)
            {
                cached = _cachedTransactionChild;
                _cachedTransactionChild = null;
            }
            cleanup.Step("SharedEngine.Dispose.cached-child", cached != null);
            cleanup.Catch(() => cached?.Dispose());
            cleanup.Step("SharedEngine.Dispose.admission");
            cleanup.Catch(_settings.SharedAdmission.Dispose);
            ThrowSharedCleanupErrors(cleanup);
        }

        private static void ThrowSharedCleanupErrors(TryCatch cleanup)
        {
            if (cleanup.Exceptions.Count == 0) return;
            var primary = cleanup.Exceptions[0];
            var suffix = 0;
            for (var i = 1; i < cleanup.Exceptions.Count; i++)
            {
                while (primary.Data.Contains("LiteDB.SharedCleanup." + suffix)) suffix++;
                primary.Data["LiteDB.SharedCleanup." + suffix++] = cleanup.Exceptions[i];
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }

        private void DisposeConnection(TryCatch cleanup)
        {
            cleanup.Step("SharedEngine.Dispose.retire-reads");
            cleanup.Catch(this.RetireCoordinatedReads);
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
                    // An executing raw-engine callback cannot join itself. ClosePin
                    // finishes its core and coordination cleanup when that call returns.
                    cleanup.Step("SharedEngine.Dispose.early-handles", _handles != null);
                    cleanup.Catch(() => _handles?.Dispose());
                    cleanup.Step("SharedEngine.Dispose.early-readers");
                    cleanup.Catch(_readers.Dispose);
                    return;
                }
                cleanup.Step("SharedEngine.Dispose.wait-pin");
                cleanup.Catch(pin.WaitReleased);
            }

            // Calls admitted before Dispose started finish first; later ones are refused.
            cleanup.Catch(this.WaitForAdmittedCalls);
            var closed = false;
#if DEBUG || TESTING
            // Wait-for graph: the close runs under every hold of this connection.
            using (LiteDB.Utils.WaitGraph.Executing(this, claimsAll: true))
#endif
            lock (_useLock)
            {
                if (_engine != null)
                {
                    // This parent's historical final checkpoint is best effort; its
                    // returned close errors do not change acknowledged WAL outcomes.
                    // A thrown admission/refusal error means this core has not closed:
                    // do not continue into dependent native-ownership release below.
                    var engine = _engine;
                    this.ObservedClose(engine, () => engine.Close(final: true));
                    _engine = null;
                    closed = true;
                }
                cleanup.Catch(this.CloseMutexSnapshotsLocked);
                _databaseUsers = 0;
            }
            // Open readers and transactions of any thread end with the connection.
            cleanup.Catch(_owner.ReleaseAll);
            // Operations left a WAL below the close threshold: checkpoint it now, so
            // the data file alone is the database again once every connection closed.
            if (!closed && !_transactionChild) cleanup.Catch(this.CheckpointOnDispose);
            cleanup.Step("SharedEngine.Dispose.handles", _handles != null);
            cleanup.Catch(() => _handles?.Dispose());
            // Leased readers may outlive the connection; the slot file closes after the last.
            cleanup.Step("SharedEngine.Dispose.readers");
            cleanup.Catch(_readers.Dispose);
            // A disposed connection holds no mutex, even for the moment its holder
            // needs to release it; another connection's final close may try it next.
            cleanup.Catch(_owner.WaitForRelease);
            cleanup.Step("SharedEngine.Dispose.coordination");
            cleanup.Catch(this.DisposeCoordination);
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
