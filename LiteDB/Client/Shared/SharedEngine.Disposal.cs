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
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0) return;

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
            cleanup.Catch(() => cached?.Dispose());
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
            cleanup.Catch(this.RetireCoordinatedReads);
            // Any thread can end a pin; its holder closes the engine and releases. Read
            // under the lock that orders a starting pin's publication with this Dispose.
            SharedMutexPin pin;
            lock (_useLock) pin = _pin;
            if (pin != null)
            {
                pin.RequestRelease(force: true);
                if (!pin.CanWaitFrom(Thread.CurrentThread))
                {
                    // An executing raw-engine callback cannot join itself. ClosePin
                    // finishes its core and coordination cleanup when that call returns.
                    cleanup.Catch(() => _handles?.Dispose());
                    cleanup.Catch(_readers.Dispose);
                    return;
                }
                cleanup.Catch(pin.WaitReleased);
            }

            // Calls admitted before Dispose started finish first; later ones are refused.
            cleanup.Catch(this.WaitForAdmittedCalls);
            var closed = false;
            lock (_useLock)
            {
                if (_engine != null)
                {
                    // This parent's historical final checkpoint is best effort; its
                    // returned close errors do not change acknowledged WAL outcomes.
                    // Keep the core published if close refuses an active raw callback;
                    // that operation still owns its normal unwind/close path.
                    cleanup.Catch(() =>
                    {
                        _engine.Close(final: true);
                        _engine = null;
                        closed = true;
                    });
                }
                cleanup.Catch(this.CloseMutexSnapshotsLocked);
                _databaseUsers = 0;
            }
            // Open readers and transactions of any thread end with the connection.
            cleanup.Catch(_owner.ReleaseAll);
            // Operations left a WAL below the close threshold: checkpoint it now, so
            // the data file alone is the database again once every connection closed.
            if (!closed && !_transactionChild) cleanup.Catch(this.CheckpointOnDispose);
            cleanup.Catch(() => _handles?.Dispose());
            // Leased readers may outlive the connection; the slot file closes after the last.
            cleanup.Catch(_readers.Dispose);
            // A disposed connection holds no mutex, even for the moment its holder
            // needs to release it; another connection's final close may try it next.
            cleanup.Catch(_owner.WaitForRelease);
            cleanup.Catch(this.DisposeCoordination);
        }

        /// <summary>
        /// Readers streaming under the mutex end with their ownership, like the
        /// operation engine: a later read would no longer be ordered with writers.
        /// </summary>
        private void CloseMutexSnapshotsLocked()
        {
            foreach (var snapshot in _mutexSnapshots) snapshot.Close(checkpoint: false);
            _mutexSnapshots.Clear();
        }
    }
}
