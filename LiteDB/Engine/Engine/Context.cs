using System;
using System.Collections.Concurrent;
using System.Linq;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        private readonly EngineContext _defaultContext;
        private readonly ConcurrentQueue<EngineContext> _abandonedContexts = new ConcurrentQueue<EngineContext>();
        internal EngineContext CurrentContext => EngineContext.CurrentFor(this) ?? _defaultContext;

        internal void BeginHandleTransaction()
        {
            using var operation = EnterOperation();
            _state.Validate();
            var transaction = _monitor.GetTransaction(true, false, out var created);
            if (!created) throw new InvalidOperationException("An explicit handle must own a new transaction.");
            transaction.ExplicitTransaction = true;
        }

        internal string[] GetTransactionCollectionNames()
        {
            using var reader = this.Query("$cols", new Query { Select = BsonExpression.Create("$") });
            var names = new System.Collections.Generic.List<string>();
            while (reader.Read())
                if (reader.Current["type"].AsString == "user") names.Add(reader.Current["name"].AsString);
            var transaction = CurrentContext.Slot.Transaction;
            if (transaction != null)
                names.AddRange(transaction.Snapshots.Where(snapshot => snapshot.CollectionPage != null)
                    .Select(snapshot => snapshot.CollectionName));
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal void AbandonContext(EngineContext context) => _abandonedContexts.Enqueue(context);

        internal void ReleaseAbandonedContexts()
        {
            // A later enqueue can be handled by the next call, just as it can arrive
            // after the final TryDequeue below. An empty queue touches no engine state.
            if (_abandonedContexts.IsEmpty) return;
            using var operation = EnterOperation(continuation: true);
            if (_state.Disposed) return;
            // A live caller keeps storage rooted. Never resurrect an unreachable
            // engine on a finalizer worker while its streams may be finalizing.
            while (_abandonedContexts.TryDequeue(out var context))
            {
                // Arbitrarily abandoned partial cursor pipelines retain their
                // existing cleanup limits; do not dispose their borrowed buffers.
                if (_monitor.Transactions.Any(transaction => ReferenceEquals(transaction.Owner.Context, context) &&
                    transaction.OpenCursors.Count != 0)) continue;
                ReleaseContext(context);
            }
        }

        internal void ReleaseContext(EngineContext context)
        {
            using var operation = EnterOperation(continuation: true);
            if (_state.Disposed) return;
            try { _monitor.ReleaseContext(context); }
            catch (Exception error) { _state.Stop(error); throw; }
        }
    }
}
