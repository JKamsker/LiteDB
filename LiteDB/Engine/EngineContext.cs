using System;
using System.Threading;

namespace LiteDB.Engine
{
    /// <summary>Connection policy and transaction ownership, independent of the shared storage core.</summary>
    internal sealed class EngineContext
    {
        [ThreadStatic] private static EngineContext _active;
        private readonly LiteEngine _engine;
        private readonly ThreadLocal<TransactionSlot> _transactions = new ThreadLocal<TransactionSlot>(() => new TransactionSlot());
        private int _references = 1;
        internal readonly EngineSettings Policy;

        internal sealed class TransactionSlot
        {
            internal TransactionService Transaction;
            internal bool ExplicitAborted;
        }

        internal EngineContext(LiteEngine engine, EngineSettings settings)
        {
            _engine = engine;
            Policy = settings.Clone();
        }

        internal static EngineContext CurrentFor(LiteEngine engine) =>
            ReferenceEquals(_active?._engine, engine) ? _active : null;
        internal TransactionSlot Slot => _transactions.Value;
        internal void DisposeSlots() => _transactions.Dispose();
        internal void Retain() => Interlocked.Increment(ref _references);

        internal void Release(bool disposing)
        {
            if (Interlocked.Decrement(ref _references) != 0) return;
            try
            {
                // An explicit context close rolls back only its own unfinished work.
                // Finalization must not perform storage I/O or wait on other users.
                if (disposing) _engine.ReleaseContext(this);
                else _engine?.AbandonContext(this);
            }
            finally { _transactions.Dispose(); }
        }

        internal Scope Enter() => new Scope(this);

        internal readonly struct Scope : IDisposable
        {
            private readonly EngineContext _previous;
            internal Scope(EngineContext context)
            {
                _previous = _active;
                _active = context;
            }
            public void Dispose() => _active = _previous;
        }
    }
}
