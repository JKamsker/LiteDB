using System;
using LiteDB.Plugins;

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
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            BsonTypeResolver.ReplaceFallbackRegistry(context);
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
            return new LiteDatabaseServices(context);
        }
    }
}
