using System;
using System.Threading;

namespace LiteDB.Engine
{
    /// <summary>Explicit dispatch identity, installed only during a bound engine call.</summary>
    internal sealed class TransactionContext
    {
        [ThreadStatic] private static TransactionContext _executing;
        [ThreadStatic] private static LiteEngine _dispatch;
        internal readonly LiteEngine Engine;
        internal readonly EngineContext Session;
        internal readonly EngineSettings Policy;
        internal readonly EngineContext.TransactionSlot Slot = new EngineContext.TransactionSlot();
        internal TransactionService Transaction;
        internal volatile LiteTransactionState Outcome = LiteTransactionState.Active;
        // Ordinary callbacks suppress binding, but still depend on this executing
        // handle returning before its locks can be released.
        internal volatile Thread ExecutingThread;

        internal TransactionContext(LiteEngine engine, EngineContext session)
        { Engine = engine; Session = session; Policy = session.Policy.Clone(); }

        internal static TransactionContext For(EngineContext session) =>
            ReferenceEquals(_executing?.Session, session) ? _executing : null;
        internal static object AdmissionOwner => (object)_executing ?? System.Threading.Thread.CurrentThread;
        internal static Scope Enter(TransactionContext transaction) => new Scope(transaction);
        internal static DispatchScope Dispatch(LiteEngine engine) => new DispatchScope(engine);
        internal static bool ConsumeDispatch(LiteEngine engine)
        {
            if (!ReferenceEquals(_dispatch, engine)) return false;
            _dispatch = null;
            return true;
        }

        // A one-use ticket authorizes a composed engine entry. It is consumed before any
        // user callback, so raw public reentry cannot acquire the enclosing handle's identity.
        internal readonly struct DispatchScope : IDisposable
        {
            private readonly LiteEngine _previous;
            internal DispatchScope(LiteEngine engine) { _previous = _dispatch; _dispatch = engine; }
            public void Dispose() => _dispatch = _previous;
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly TransactionContext _previous, _transaction;
            private readonly Thread _previousThread;
            internal Scope(TransactionContext transaction)
            {
                _previous = _executing;
                _transaction = transaction;
                _previousThread = transaction?.ExecutingThread;
                if (transaction != null) transaction.ExecutingThread = Thread.CurrentThread;
                _executing = transaction;
#if DEBUG || TESTING
                // Proof overlay (PR #133): a bound call executes its transaction on this thread; no other
                // thread can end the transaction's holds while it runs (overlapping calls are refused).
                LiteDB.Utils.WaitGraph.Enter(transaction, claimsAll: true);
#endif
            }
            public void Dispose()
            {
#if DEBUG || TESTING
                LiteDB.Utils.WaitGraph.Exit(_transaction);
#endif
                _executing = _previous;
                if (_transaction != null) _transaction.ExecutingThread = _previousThread;
            }
        }
    }
}
