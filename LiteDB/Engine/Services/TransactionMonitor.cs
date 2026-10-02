using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteDB.Utils;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// This class monitor all open transactions to manage memory usage for each transaction
    /// [Singleton - ThreadSafe]
    /// </summary>
    internal class TransactionMonitor : IDisposable
    {
        private readonly TransactionRegistry _transactions = new TransactionRegistry();
        private readonly Func<EngineContext> _context;
        private readonly EngineContext _standaloneContext;
        internal EngineContext CurrentContext => _context?.Invoke() ?? _standaloneContext;

        private readonly HeaderPage _header;
        private readonly LockService _locker;
        private readonly DiskService _disk;
        private readonly WalIndexService _walIndex;

        private int _disposed;

#if TESTING
        internal Action BeforeTransactionRegistration { get; set; }
#endif
#if DEBUG || TESTING
        internal Action AfterTransactionExit { get; set; }
#endif

        // expose open transactions
        public ICollection<TransactionService> Transactions => _transactions.Snapshot();
        public int TransactionPageLimit => CurrentContext.Policy.TransactionPageLimit;
        public TransactionService[] GetTransactionsSnapshot() => _transactions.Snapshot().ToArray();

        public TransactionMonitor(HeaderPage header, LockService locker, DiskService disk, WalIndexService walIndex, int transactionPageLimit, Func<EngineContext> context = null)
        {
            if (transactionPageLimit <= 0) throw new ArgumentOutOfRangeException(nameof(transactionPageLimit));

            _header = header;
            _locker = locker;
            _disk = disk;
            _walIndex = walIndex;
            _context = context;
            if (context == null) _standaloneContext = new EngineContext(null, new EngineSettings { TransactionPageLimit = transactionPageLimit });
        }

        public TransactionService GetTransaction(bool create, bool queryOnly, out bool isNew)
        {
            this.ThrowIfDisposed();
            var context = CurrentContext;
            var slot = context.Slot;
            var transaction = slot.Transaction;

            if (create && transaction == null)
            {
                isNew = true;

#if TESTING
                BeforeTransactionRegistration?.Invoke();
#endif
                this.ThrowIfDisposed();

                var enteredTransaction = false;
                var owner = (object)TransactionContext.For(context) ?? Thread.CurrentThread;
                try
                {
                    // Checkpoint can reset the WAL ID sequence only while holding
                    // exclusive admission. Take our lease before reserving an ID.
                    _locker.EnterTransaction(owner);
                    enteredTransaction = true;
                    this.ThrowIfDisposed();
                    transaction = new TransactionService(_header, _locker, _disk, _walIndex, context.Policy.TransactionPageLimit, this, queryOnly);
                    _transactions.Add(transaction);
                    var explicitContext = TransactionContext.For(context);
                    if (explicitContext != null) explicitContext.Transaction = transaction;

                    this.ThrowIfDisposed();
                    if (queryOnly == false) slot.Transaction = transaction;
                }
                catch
                {
                    if (transaction != null) _transactions.Remove(transaction);
                    try
                    {
                        transaction?.Dispose();
                    }
                    finally
                    {
                        if (enteredTransaction) _locker.ExitTransaction(owner);
                    }
                    throw;
                }
            }
            else
            {
                isNew = false;
            }

            return transaction;
        }

        /// <summary>
        /// Dispose and remove transaction from monitor
        /// without releasing thread lock
        /// </summary>
        private void RemoveTransaction(TransactionService transaction, out bool removed)
        {
            removed = false;
            try
            {
                transaction.Dispose();
            }
            finally
            {
                removed = _transactions.Remove(transaction);
            }
        }

        /// <summary>
        /// Release current thread transaction
        /// </summary>
        public void ReleaseTransaction(TransactionService transaction)
        {
            if (!transaction.QueryOnly)
                ENSURE((transaction.Owner.Explicit != null || transaction.OwnerThread == Thread.CurrentThread) && transaction.Owner.Slot.Transaction == transaction,
                    "current thread must contains transaction parameter");
            var removed = false;
            try
            {
                this.RemoveTransaction(transaction, out removed);
            }
            finally
            {
                try
                {
                    // Rebuild may dispose the monitor and disk as soon as the transaction
                    // lease is released. Finish service cleanup before admitting it.
                    if (!transaction.QueryOnly)
                    {
                        ENSURE((transaction.Owner.Explicit != null || transaction.OwnerThread == Thread.CurrentThread) && transaction.Owner.Slot.Transaction == transaction,
                            "current thread must contains transaction parameter");
                        transaction.Owner.Slot.Transaction = null;
                    }
                    _disk.Cache.TrimToLimit();
                }
                finally
                {
                    if (removed)
                    {
                        _locker.ExitTransaction(transaction.Owner.Admission);
#if DEBUG || TESTING
                        AfterTransactionExit?.Invoke();
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Remember that a failed operation rolled back this thread's explicit transaction, so that
        /// the caller's pending Commit is not mistaken for a completion from a foreign thread.
        /// </summary>
        public void MarkExplicitAbort()
        {
            // Dispose on another thread may already have released the slot; a closing engine has nothing left to complete.
            try
            {
                CurrentContext.Slot.ExplicitAborted = true;
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Read and clear the mark left by <see cref="MarkExplicitAbort"/> on the current thread.
        /// </summary>
        public bool ConsumeExplicitAbort()
        {
            try
            {
                var aborted = CurrentContext.Slot.ExplicitAborted;
                if (aborted) CurrentContext.Slot.ExplicitAborted = false;
                return aborted;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Get transaction from current thread (from thread slot or from queryOnly) - do not created new transaction
        /// Used only in SystemCollections to get running query transaction
        /// </summary>
        public TransactionService GetThreadTransaction()
        {
            this.ThrowIfDisposed();
            return CurrentContext.Slot.Transaction ?? _transactions.FindForThread(Thread.CurrentThread, CurrentContext);
        }

        /// <summary>
        /// Check whether a transaction reached its fixed page-retention limit.
        /// </summary>
        public bool CheckSafepoint(TransactionService trans)
        {
            return trans.Pages.TransactionSize >= trans.MaxTransactionSize;
        }

        /// <summary>
        /// Dispose all open transactions
        /// </summary>
        [TeardownPath("TransactionMonitor.Dispose", TeardownDisposition.Propagated,
            "Every step runs in TryCatch; collected failures are thrown as one AggregateException (TransactionMonitor.cs Dispose).")]
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var cleanup = new LiteDB.Utils.TryCatch();
            foreach (var transaction in _transactions.Close())
            {
                Reachability.Sometimes("maintenance:close-during-active-transaction");
#if DEBUG || TESTING
                if (!ReferenceEquals(transaction.OwnerThread, Thread.CurrentThread)) Reachability.Sometimes("maintenance:close-during-foreign-transaction");
#endif
                cleanup.Step("TransactionMonitor.Dispose.transaction");
                cleanup.Catch(transaction.Dispose);
                if (ReferenceEquals(transaction.Owner.Slot.Transaction, transaction)) transaction.Owner.Slot.Transaction = null;
            }

            _standaloneContext?.Release(disposing: false);
            if (cleanup.Exceptions.Count > 0) throw new AggregateException(cleanup.Exceptions);
        }

        internal void ReleaseContext(EngineContext context)
        {
            foreach (var transaction in _transactions.Snapshot().Where(item => ReferenceEquals(item.Owner.Context, context)))
            {
                if (transaction.State == TransactionState.Active) transaction.Rollback();
                try { this.RemoveTransaction(transaction, out _); }
                finally
                {
                    if (ReferenceEquals(transaction.Owner.Slot.Transaction, transaction)) transaction.Owner.Slot.Transaction = null;
                    _locker.ExitTransaction(transaction.Owner.Admission);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(TransactionMonitor));
        }
    }
}
