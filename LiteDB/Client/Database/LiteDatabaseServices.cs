using System;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;

namespace LiteDB
{
    /// <summary>
    /// Provides access to per-database services that plugins can extend.
    /// </summary>
    public sealed class LiteDatabaseServices
    {
        private readonly ILitePluginContext _context;
        private static readonly Lazy<LiteDatabaseServices> _default = new Lazy<LiteDatabaseServices>(CreateDefault, true);

        internal LiteDatabaseServices(ILitePluginContext context)
            : this(context, applyFallback: true)
        {
        }

        private LiteDatabaseServices(ILitePluginContext context, bool applyFallback)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (applyFallback)
            {
                BsonTypeResolver.ReplaceFallbackRegistry(context);
                PageFactoryResolver.ReplaceFallbackRegistry(context);
            }
        }

        /// <summary>
        /// Gets the expression registry associated with the database.
        /// </summary>
        public IExpressionRegistry ExpressionRegistry => _context.Expressions;

        /// <summary>
        /// Gets the index registry associated with the database.
        /// </summary>
        public IIndexRegistry IndexRegistry => _context.Indexes;

        /// <summary>
        /// Gets the query planner registry associated with the database.
        /// </summary>
        public IQueryPlannerRegistry QueryPlanner => _context.QueryPlanner;

        /// <summary>
        /// Gets the LINQ resolver registry associated with the database.
        /// </summary>
        public ILinqResolverRegistry LinqResolvers => _context.LinqResolvers;

        /// <summary>
        /// Gets the index interceptor registry associated with the database.
        /// </summary>
        public IIndexInterceptorRegistry IndexInterceptors => _context.IndexInterceptors;

        /// <summary>
        /// Gets the vector index strategy registry associated with the database.
        /// </summary>
        public IVectorIndexStrategyRegistry VectorIndexes => _context.VectorIndexes;

        /// <summary>
        /// Gets the service provider exposed to plugins.
        /// </summary>
        public IServiceProvider Services => _context.Services;

        /// <summary>
        /// Gets the logger that plugins can use during initialization.
        /// </summary>
        public ILogger Logger => _context.Logger;

        /// <summary>
        /// Gets the connection string used to configure the database.
        /// </summary>
        public ConnectionString ConnectionString => _context.ConnectionString;

        internal ILitePluginContext Context => _context;

        internal static LiteDatabaseServices Default => _default.Value;

        private static LiteDatabaseServices CreateDefault()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
            var services = new LiteDatabaseServices(context, applyFallback: false);
            BsonTypeResolver.ReplaceFallbackRegistry(context);
            PageFactoryResolver.ReplaceFallbackRegistry(context);
            return services;
        }
    }
}
