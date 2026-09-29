using System;

namespace LiteDB.Engine
{
    /// <summary>Explicit dispatch identity, installed only during a bound engine call.</summary>
    internal sealed class TransactionContext
    {
        [ThreadStatic] private static TransactionContext _executing;
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

        internal readonly struct Scope : IDisposable
        {
            private readonly TransactionContext _previous;
            internal Scope(TransactionContext transaction) { _previous = _executing; _executing = transaction; }
            public void Dispose() => _executing = _previous;
        }
    }
}
