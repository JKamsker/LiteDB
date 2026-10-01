using System;
using System.Linq;
using LiteDB.Engine;
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
            if (!disposing) return;
            lock (_useLock)
            {
                if (_disposed != 0) return;
                this.ThrowIfClosingFromOperation();
                Volatile.Write(ref _disposed, 1);
            }

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
            bool closed;
            using (this.OwnershipFrame(() => _owner.IsHeld))
#if DEBUG || TESTING
            // Wait-for graph: the close runs under every hold of this connection.
            using (LiteDB.Utils.WaitGraph.Executing(this, claimsAll: true))
#endif
                closed = this.CloseOwnedCores(checkpoint: true, final: true);
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

        internal void ThrowIfClosingFromOperation()
        {
            lock (_useLock)
            {
                // Leased snapshots are independent and may outlive this facade.
                if (this.AdmittedDepth() != 0 || this.IsExecutingOwnedCoreOnCurrentThread())
                    throw new InvalidOperationException("Cannot close a shared connection from inside its executing operation.");
            }
        }

        private bool IsForeignReaderCallback()
        {
            if (_owner.IsOwnedByCurrentThread) return false;
            var pin = _pin;
            if (pin != null && ReferenceEquals(pin.Owner, Thread.CurrentThread)) return false;
            return this.IsExecutingOwnedCoreOnCurrentThread();
        }

        private bool IsExecutingOwnedCoreOnCurrentThread()
        {
            lock (_useLock)
                return _engine?.IsExecutingOnCurrentThread == true ||
                    _mutexSnapshots.Any(snapshot => snapshot.IsExecutingOnCurrentThread);
        }

        /// <summary>Keep ownership published while draining, without a callback-needed lock.</summary>
        private bool CloseOwnedCores(bool checkpoint, bool final = false)
        {
            LiteEngine core;
            LiteEngine[] snapshots;
            lock (_useLock)
            {
                core = _engine;
                snapshots = _mutexSnapshots.ToArray();
            }
            // Returned errors follow completed teardown and keep the parent's
            // best-effort policy. A thrown refusal must prevent native release.
            if (core != null) this.ObservedClose(core, () => core.Close(checkpoint: checkpoint, final: final));
            foreach (var snapshot in snapshots) this.ObservedClose(snapshot, () => snapshot.Close(checkpoint: false));
            lock (_useLock)
            {
                if (ReferenceEquals(_engine, core)) _engine = null;
                foreach (var snapshot in snapshots) _mutexSnapshots.Remove(snapshot);
                _databaseUsers = 0;
            }
            return core != null;
        }
    }
}
