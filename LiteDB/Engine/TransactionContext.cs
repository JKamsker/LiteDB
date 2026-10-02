using System;

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
            private readonly TransactionContext _previous;
#if DEBUG || TESTING
            // Proof overlay (PR #133): a bound call executes its transaction on this thread; no other
            // thread can end the transaction's holds while it runs (overlapping calls are refused).
            private readonly TransactionContext _graph;
            internal Scope(TransactionContext transaction)
            {
                _previous = _executing; _executing = transaction; _graph = transaction;
                LiteDB.Utils.WaitGraph.Enter(transaction, claimsAll: true);
            }
            public void Dispose() { LiteDB.Utils.WaitGraph.Exit(_graph); _executing = _previous; }
#else
            internal Scope(TransactionContext transaction) { _previous = _executing; _executing = transaction; }
            public void Dispose() => _executing = _previous;
#endif
        }
    }
}
