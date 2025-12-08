using System;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using LiteDB.Plugins.Query;

namespace LiteDB
{
    /// <summary>
    /// Provides access to per-database services that plugins can extend.
    /// </summary>
    public sealed class LiteDatabaseServices
    {
        private readonly ILitePluginContext _context;

        internal LiteDatabaseServices(ILitePluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
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
        /// Gets the query metadata accessor associated with the database.
        /// </summary>
        public IQueryMetadataAccessor QueryMetadata => _context.QueryMetadata;

        /// <summary>
        /// Gets the LINQ resolver registry associated with the database.
        /// </summary>
        public ILinqResolverRegistry LinqResolvers => _context.LinqResolvers;

        /// <summary>
        /// Gets the custom index strategy registry associated with the database.
        /// </summary>
        public ICustomIndexStrategyRegistry CustomIndexes => _context.CustomIndexes;

        /// <summary>
        /// Gets the SQL function registry associated with the database.
        /// </summary>
        public ISqlFunctionRegistry SqlFunctions => _context.SqlFunctions;

        /// <summary>
        /// Gets the query operator registry associated with the database.
        /// </summary>
        public IQueryOperatorRegistry QueryOperators => _context.QueryOperators;

        /// <summary>
        /// Gets the query cost model registry associated with the database.
        /// </summary>
        public IQueryCostModelRegistry QueryCostModels => _context.QueryCostModels;

        /// <summary>
        /// Gets the plugin index metadata registry associated with the database.
        /// </summary>
        public IPluginIndexMetadataRegistry IndexMetadata => _context.IndexMetadata;

        /// <summary>
        /// Gets the plugin diagnostic policy associated with the database.
        /// </summary>
        public IPluginDiagnosticPolicy DiagnosticPolicy => _context.DiagnosticPolicy;

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
    }
}


