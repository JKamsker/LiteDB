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

        internal void AbandonContext(EngineContext context) => _abandonedContexts.Enqueue(context);

        internal void ReleaseAbandonedContexts()
        {
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
            if (_state.Disposed) return;
            try { _monitor.ReleaseContext(context); }
            catch (Exception error) { _state.Stop(error); throw; }
        }
    }
}
