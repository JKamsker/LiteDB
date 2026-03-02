using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Initialize a new transaction. Transaction are created "per-thread". There is only one single transaction per thread.
        /// Return true if transaction was created or false if current thread already in a transaction.
        /// </summary>
        public bool BeginTrans()
        {
            _state.Validate();

            var transacion = _monitor.GetTransaction(true, false, out var isNew);

            transacion.ExplicitTransaction = true;

            if (transacion.OpenCursors.Count > 0) throw new LiteException(0, "This thread contains an open cursors/query. Close cursors before Begin()");

            LOG(isNew, $"begin trans", "COMMAND");

            return isNew;
        }

        /// <summary>
        /// Persist all dirty pages into LOG file
        /// </summary>
        public bool Commit()
        {
            _state.Validate();

            var transaction = _monitor.GetTransaction(false, false, out _);

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
        public bool Rollback()
        {
            _state.Validate();

            var transaction = _monitor.GetTransaction(false, false, out _);

            if (transaction != null && transaction.State == TransactionState.Active)
            {
                try
                {
                    transaction.Rollback();
                    return true;
                }
                finally
                {
                    _monitor.ReleaseTransaction(transaction);
                }
            }

            return false;
        }

        /// <summary>
        /// Create (or reuse) a transaction an add try/catch block. Commit transaction if is new transaction
        /// </summary>
        private T AutoTransaction<T>(Func<TransactionService, T> fn)
        {
            _state.Validate();

            var transaction = _monitor.GetTransaction(true, false, out var isNew);

            T result;

            try
            {
                result = fn(transaction);
            }
            catch (Exception ex)
            {
                var shouldHandle = _state.Handle(ex);

                if (shouldHandle && (isNew || transaction.ExplicitTransaction))
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception rollbackEx)
                    {
                        _state.Handle(rollbackEx);
                    }

                    try
                    {
                        _monitor.ReleaseTransaction(transaction);
                    }
                    catch (Exception releaseEx)
                    {
                        _state.Handle(releaseEx);
                    }
                }

                throw;
            }

            if (isNew)
            {
                try
                {
                    this.CommitAndReleaseTransaction(transaction);
                }
                catch (Exception ex)
                {
                    _state.Handle(ex);
                    throw;
                }
            }

            return result;
        }

        private void CommitAndReleaseTransaction(TransactionService transaction)
        {
            var committed = false;
            Exception commitException = null;

            try
            {
                transaction.Commit();
                committed = true;
            }
            catch (Exception ex)
            {
                commitException = ex;
                throw;
            }
            finally
            {
                try
                {
                    _monitor.ReleaseTransaction(transaction);
                }
                catch (Exception releaseEx)
                {
                    if (commitException != null)
                    {
                        _state.Handle(releaseEx);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            // try checkpoint when finish transaction and log file are bigger than checkpoint pragma value (in pages)
            if (committed &&
                _header.Pragmas.Checkpoint > 0 &&
                _disk.GetFileLength(FileOrigin.Log) >= (_header.Pragmas.Checkpoint * PAGE_SIZE))
            {
                _walIndex.TryCheckpoint();
            }
        }
    }
}
