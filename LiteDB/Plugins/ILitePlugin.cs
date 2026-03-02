using System;
using System.Collections.Generic;
using LiteDB.Engine;
using LiteDB.Plugins.Bson;
using LiteDB.Plugins.Indexing;
using LiteDB.Plugins.Query;
using LiteDB.Plugins.Storage;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Represents an extension that can participate in <see cref="LiteDatabase"/> initialization.
    /// </summary>
    public interface ILitePlugin
    {
        /// <summary>
        /// Called once during database construction allowing the plugin to register behaviours.
        /// When using <see cref="LiteDatabaseBuilder.BuildFactory"/>, the provided <paramref name="database"/> can be an internal host handle used
        /// only for initialization. Plugins should avoid capturing it for later use and instead implement <see cref="ILiteDatabaseHandleLifecycle"/>
        /// to observe user-facing handles created by the factory.
        /// </summary>
        /// <param name="database">The database that is being configured.</param>
        /// <param name="context">The plugin context exposing registration entry points.</param>
        void Initialize(LiteDatabase database, ILitePluginContext context);
    }

    /// <summary>
    /// Optional hook invoked when a database handle is created via <see cref="LiteDatabaseBuilder"/> or <see cref="ILiteDatabaseFactory"/>.
    /// </summary>
    /// <remarks>
    /// This hook is invoked for handles created by <see cref="LiteDatabaseBuilder.Build"/>, as well as handles created by factories
    /// (<see cref="LiteDatabaseBuilder.BuildFactory"/> and <see cref="ILiteDatabaseFactory.CreateDatabase"/>). It is not invoked for handles created
    /// directly via <c>new LiteDatabase(...)</c>.
    ///
    /// <para>
    /// Ordering: For builder-created handles, <see cref="ILitePlugin.Initialize"/> is invoked first and then <see cref="OnHandleCreated"/> is called.
    /// For factory-created handles, <see cref="ILitePlugin.Initialize"/> is invoked once during factory construction (potentially with an internal host handle),
    /// and <see cref="OnHandleCreated"/> is called for each user-facing handle created by the factory.
    /// </para>
    /// </remarks>
    public interface ILiteDatabaseHandleLifecycle
    {
        /// <summary>
        /// Invoked after a database handle has been created. Implementations must be thread-safe because factories can create handles concurrently.
        /// </summary>
        void OnHandleCreated(ILiteDatabase database);
    }

    /// <summary>
    /// Provides services and registries that plugins can interact with.
    /// </summary>
    public interface ILitePluginContext
    {
        IExpressionRegistry Expressions { get; }

        IIndexRegistry Indexes { get; }

        IEnsureIndexInterceptorRegistry EnsureIndexInterceptors { get; }

        IQueryPlannerRegistry QueryPlanner { get; }

        IQueryMetadataAccessor QueryMetadata { get; }

        IPluginDiagnosticPolicy DiagnosticPolicy { get; }

        ISqlFunctionRegistry SqlFunctions { get; }

        IQueryOperatorRegistry QueryOperators { get; }

        IQueryCostModelRegistry QueryCostModels { get; }

        ICustomBsonTypeRegistry BsonTypes { get; }

        IPageTypeRegistry PageFactories { get; }

        IPluginIndexMetadataRegistry IndexMetadata { get; }

        ICustomIndexStrategyRegistry CustomIndexes { get; }

        ILinqResolverRegistry LinqResolvers { get; }

        IServiceProvider Services { get; }

        ILogger Logger { get; }

        ConnectionString ConnectionString { get; }

        void RegisterQueryMetadata(string pluginId, int version, IReadOnlyCollection<string> reservedKeys);

        bool TryGetQueryMetadataDescriptor(string pluginId, out QueryMetadataDescriptor descriptor);

        QueryMetadataDescriptor GetQueryMetadataDescriptor(string pluginId);

        void SetDiagnosticPolicy(IPluginDiagnosticPolicy policy);

        void RegisterBsonType(CustomBsonTypeDescriptor registration);

        bool TryGetBsonType(byte typeCode, out CustomBsonTypeDescriptor registration);

        bool TryGetBsonType(string name, out CustomBsonTypeDescriptor registration);

        void RegisterCustomIndexStrategy(CustomIndexStrategyDescriptor descriptor);

        bool TryGetCustomIndexStrategyDescriptor(string strategyId, out CustomIndexStrategyDescriptor descriptor);

        CustomIndexStrategyDescriptor GetCustomIndexStrategyDescriptor(string strategyId);

        void RegisterPageFactory(PageFactoryRegistration registration);

        bool TryGetPageFactory(string pageType, out PageFactoryRegistration registration);

        void RegisterSqlFunction(SqlFunctionRegistration registration);

        void RegisterQueryOperator(QueryOperatorRegistration registration);

        void RegisterQueryCostModel(QueryCostModelRegistration registration);

        void RegisterIndexMetadata(PluginIndexMetadataDescriptor descriptor);

        bool TryGetIndexMetadataDescriptor(string indexKind, out PluginIndexMetadataDescriptor descriptor);

        PluginIndexMetadataDescriptor GetIndexMetadataDescriptor(string indexKind);
    }

    /// <summary>
    /// Represents a logger implementation that plugins can use during initialization.
    /// </summary>
    public interface ILogger
    {
        void Write(LogLevel level, string message, Exception exception = null);
    }

    /// <summary>
    /// Minimal log level enumeration used by the plugin logger abstraction.
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5
    }

    /// <summary>
    /// Defines a registry for custom expression operators.
    /// </summary>
    public interface IExpressionRegistry
    {
        void RegisterBinaryOperator(string token, BsonExpressionType expressionType, BsonBinaryOperator implementation, BinaryOperatorPrecedence precedence = BinaryOperatorPrecedence.Comparison, string source = null);

        void RegisterFunction(string name, Delegate implementation, BsonExpressionType expressionType, bool convertScalarLeftToEnumerable = true, bool isScalarResult = false);

        void RegisterKeyword(string keyword);

        IReadOnlyCollection<BinaryOperatorRegistration> Operators { get; }

        IReadOnlyCollection<ExpressionFunctionRegistration> Functions { get; }

        IReadOnlyCollection<string> Keywords { get; }

        IQueryOperatorRegistry QueryOperators { get; }

        bool ContainsOperator(string token);

        bool ContainsKeyword(string keyword);
    }

    /// <summary>
    /// Represents the implementation contract for index strategies contributed by plugins.
    /// </summary>
    public interface IIndexStrategy
    {
        string Kind { get; }

        byte IndexTypeCode { get; }

        bool EnsureIndex(object snapshot, object collection, string name, BsonExpression expression, BsonDocument options);

        bool DropIndex(object snapshot, object collection, string name);

        void OnDocumentUpsert(object snapshot, object collection, object dataBlock, BsonDocument document);

        void OnDocumentDelete(object snapshot, object collection, object dataBlock);
    }

    /// <summary>
    /// Registry that exposes installed <see cref="IIndexStrategy"/> implementations.
    /// </summary>
    public interface IIndexRegistry
    {
        void Register(IIndexStrategy strategy);

        IIndexStrategy GetByKind(string kind);

        IIndexStrategy GetByType(byte indexType);

        IEnumerable<IIndexStrategy> All { get; }
    }

    /// <summary>
    /// Intercepts EnsureIndex requests so plugins can provide custom provisioning.
    /// </summary>
    public interface IEnsureIndexInterceptor
    {
        bool TryHandleEnsureIndex(EnsureIndexContext context);
    }

    /// <summary>
    /// Registry responsible for orchestrating <see cref="IEnsureIndexInterceptor"/> implementations.
    /// </summary>
    public interface IEnsureIndexInterceptorRegistry
    {
        void Add(IEnsureIndexInterceptor interceptor, int order = 0);

        int Count { get; }

        IEnumerable<IEnsureIndexInterceptor> Interceptors { get; }
    }

    /// <summary>
    /// Represents a query planning rule that can participate in index selection.
    /// </summary>
    public interface IQueryPlanningRule
    {
        /// <summary>
        /// Attempts to rewrite the query plan by selecting an index implementation.
        /// </summary>
        /// <param name="context">Planning context describing the query being optimized.</param>
        /// <returns>
        /// True when the rule has selected an index via <see cref="QueryPlanningContext.UseIndex"/> and wants to stop rule evaluation.
        /// Returning false indicates that the rule did not apply and evaluation should continue.
        /// </returns>
        bool TryRewrite(QueryPlanningContext context);
    }

    /// <summary>
    /// Registry responsible for orchestrating <see cref="IQueryPlanningRule"/> implementations.
    /// </summary>
    public interface IQueryPlannerRegistry
    {
        /// <summary>
        /// Adds a query planning rule to the optimization pipeline.
        /// </summary>
        /// <param name="rule">The rule instance to register.</param>
        /// <param name="order">
        /// Sort key controlling evaluation order. Lower values run first; rules with the same order run in the order they were registered.
        /// The first rule that returns true after calling <see cref="QueryPlanningContext.UseIndex"/> is selected and later rules are not evaluated.
        /// </param>
        void AddRule(IQueryPlanningRule rule, int order = 0);

        /// <summary>
        /// Gets the currently registered rules in evaluation order.
        /// </summary>
        IEnumerable<IQueryPlanningRule> Rules { get; }
    }

    /// <summary>
    /// Factory delegate used to construct LINQ type resolvers for a specific database instance.
    /// </summary>
    /// <param name="database">The database requesting the resolver.</param>
    /// <returns>The resolver associated with the requested type.</returns>
    public delegate ITypeResolver LinqResolverFactory(LiteDatabase database);

    /// <summary>
    /// Registry that manages LINQ resolver factories contributed by plugins.
    /// </summary>
    public interface ILinqResolverRegistry
    {
        void Register(Type targetType, LinqResolverFactory factory);

        bool TryGetFactory(Type targetType, out LinqResolverFactory factory);

        IReadOnlyCollection<Type> RegisteredTypes { get; }
    }

    /// <summary>
    /// Delegate describing a binary operator participating in expression evaluation.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The resulting value.</returns>
    public delegate BsonValue BsonBinaryOperator(BsonValue left, BsonValue right);

    internal interface IPluginHost
    {
        void SetPluginContext(ILitePluginContext context);
    }
}
