using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LiteDB.Utils;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Initialize a transaction for the current engine context and actual thread.
        /// Return true when created; false joins the current thread transaction. Keep the block synchronous, with no await.
        /// </summary>
        public bool BeginTrans()
        {
            using var operation = EnterPublicOperation();
            _state.Validate();

            if (CurrentContext.Policy.ReadOnly) throw new ReadOnlyContextException("Cannot start a transaction in a read-only database.");

            var transacion = _monitor.GetTransaction(true, false, out var isNew);

            if (transacion.OpenCursors.Count > 0) throw new LiteException(0, "This thread contains an open cursors/query. Close cursors before Begin()");

            if (isNew) transacion.ExplicitTransaction = true;

            _monitor.ConsumeExplicitAbort();

            LOG(isNew, $"begin trans", "COMMAND");

            return isNew;
        }

        /// <summary>
        /// Persist all dirty pages into LOG file
        /// </summary>
        public bool Commit()
        {
            using var operation = EnterPublicOperation();
            _state.Validate();

            var transaction = this.GetTransactionForCompletion(commit: true);

            if (transaction != null)
            {
                // do not accept explicit commit transaction when contains open cursors running
                if (transaction.OpenCursors.Count > 0) throw new LiteException(0, "Current transaction contains open cursors. Close cursors before run Commit()");

                if (transaction.State == TransactionState.Active)
                {
                    this.CommitAndReleaseTransaction(transaction);

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Do rollback to current transaction. Clear dirty pages in memory and return new pages to main empty linked-list
        /// </summary>
        public bool Rollback() => Rollback(cleanup: false);

        internal bool RollbackHandleOnDispose() => Rollback(cleanup: true);

        private bool Rollback(bool cleanup)
        {
            using var operation = EnterPublicOperation();
            if (cleanup)
            {
                // Fatal publication makes the core unusable before retained readers
                // allow physical teardown. The fatal owner already owns this cleanup.
                if (_state.IsUnavailable) return false;
            }
            else _state.Validate();

            var transaction = this.GetTransactionForCompletion(commit: false);

            if (transaction != null && transaction.State == TransactionState.Active)
            {
                this.RollbackAndReleaseTransaction(transaction, cleanup);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Create (or reuse) a transaction an add try/catch block. Commit transaction if is new transaction
        /// </summary>
        private T AutoTransaction<T>(Func<TransactionService, T> fn) => this.ExecuteAutoTransaction(fn, true);

        private T AutoReadTransaction<T>(Func<TransactionService, T> fn) => this.ExecuteAutoTransaction(fn, false);

        private T ExecuteAutoTransaction<T>(Func<TransactionService, T> fn, bool write)
        {
            _state.Validate();

            if (write && CurrentContext.Policy.ReadOnly) throw new ReadOnlyContextException("Cannot modify a read-only database.");

            var transaction = _monitor.GetTransaction(true, false, out var isNew);

            try
            {
                var result = fn(transaction);

                // if this transaction was auto-created for this operation, commit & dispose now
                if (isNew)
                    this.CommitAndReleaseTransaction(transaction);

                return result;
            }
            catch(Exception ex)
            {
                if (_state.Handle(ex) && transaction.State == TransactionState.Active)
                {
                    try { this.RollbackAndReleaseTransaction(transaction); }
                    catch (Exception cleanup) { ex.Data["LiteDB.StatementRollback"] = cleanup; }
                    finally { if (transaction.ExplicitTransaction) _monitor.MarkExplicitAbort(); }
                }

                throw;
            }
        }

        private void CommitAndReleaseTransaction(TransactionService transaction)
        {
            try
            {
                transaction.Commit();
                _monitor.ReleaseTransaction(transaction);
            }
            catch (Exception ex)
            {
                Reachability.Sometimes("maintenance:fatal-during-commit");
#if DEBUG || TESTING
                if (_state.Disposed) Reachability.Sometimes("maintenance:commit-cut-off-by-stopped-engine");
#endif
                // Completion may have partially persisted state. Do not let a later
                // write reuse this transaction and report success without committing.
                _state.Stop(ex);
                throw;
            }

            // try checkpoint when finish transaction and log file are bigger than checkpoint pragma value (in pages)
            if (!CurrentContext.Policy.ReadOnly && _header.Pragmas.Checkpoint > 0 &&
                _disk.GetFileLength(FileOrigin.Log) >= (_header.Pragmas.Checkpoint * PAGE_SIZE))
            {
                _walIndex.TryAutoCheckpoint();
            }
        }

        private void RollbackAndReleaseTransaction(TransactionService transaction, bool cleanup = false)
        {
            try
            {
                transaction.Rollback();
                _monitor.ReleaseTransaction(transaction);
            }
            catch (Exception ex)
            {
                // A peer can publish failure after the initial availability check.
                // Compare BEFORE Stop: Stop may publish this operation's own error
                // unchanged (e.g. INVALID_DATAFILE_STATE), which must still escape.
                if (cleanup && _state.IsPublishedFailure(ex)) return;
                _state.Stop(ex);
                throw;
            }
        }
    }
}
