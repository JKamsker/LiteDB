using System;
using LiteDB.Engine;

namespace LiteDB
{
    /// <summary>
    /// Immutable services owned by a database and shared by its client objects.
    /// </summary>
    internal sealed class LiteDatabaseContext
    {
        public ILiteEngine Engine { get; }
        internal ILiteEngine RawEngine { get; }

        public BsonMapper Mapper { get; private set; }
        internal void ReleaseMapper() => Mapper = null;

        public LiteDatabaseContext(ILiteEngine engine, BsonMapper mapper, SessionLifetime lifetime = null)
        {
            RawEngine = engine;
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            // Internal bound adapters already run under LiteTransaction.Run. Ordinary
            // database calls still clear any ambient binding through SessionEngine.
            this.Engine = engine is TransactionEngine ? engine : new SessionEngine(engine, lifetime);
            this.Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }
    }
}
