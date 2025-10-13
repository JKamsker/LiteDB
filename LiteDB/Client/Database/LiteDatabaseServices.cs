using System;
using LiteDB.Plugins;

namespace LiteDB
{
    /// <summary>
    /// Aggregates per-database services exposed by the plugin infrastructure.
    /// </summary>
    public sealed class LiteDatabaseServices
    {
        private readonly ILitePluginContext _context;

        internal LiteDatabaseServices(ILitePluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Gets the expression registry associated with this database instance.
        /// </summary>
        public IExpressionRegistry ExpressionRegistry => _context.Expressions;

        /// <summary>
        /// Gets the index registry associated with this database instance.
        /// </summary>
        public IIndexRegistry Indexes => _context.Indexes;

        /// <summary>
        /// Gets the query planner registry associated with this database instance.
        /// </summary>
        public IQueryPlannerRegistry QueryPlanner => _context.QueryPlanner;

        /// <summary>
        /// Gets the service provider configured for this database instance.
        /// </summary>
        public IServiceProvider ServiceProvider => _context.Services;

        /// <summary>
        /// Gets the logger configured for this database instance.
        /// </summary>
        public ILogger Logger => _context.Logger;

        /// <summary>
        /// Gets the connection string associated with this database instance.
        /// </summary>
        public ConnectionString ConnectionString => _context.ConnectionString;
    }
}
