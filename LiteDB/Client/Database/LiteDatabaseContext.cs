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
            this.Engine = new SessionEngine(engine ?? throw new ArgumentNullException(nameof(engine)), lifetime);
            this.Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }
    }
}
