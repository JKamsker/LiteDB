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
        /// </summary>
        /// <param name="database">The database that is being configured.</param>
        /// <param name="context">The plugin context exposing registration entry points.</param>
        void Initialize(LiteDatabase database, ILitePluginContext context);
    }

    /// <summary>
    /// Provides services and registries that plugins can interact with.
    /// </summary>
    public interface ILitePluginContext
    {
        IExpressionRegistry Expressions { get; }

        IIndexRegistry Indexes { get; }

        IQueryPlannerRegistry QueryPlanner { get; }

        IQueryMetadataAccessor QueryMetadata { get; }

        IBsonTypeRegistry BsonTypes { get; }

        IPageFactoryRegistry PageFactories { get; }

        IVectorIndexStrategyRegistry VectorIndexes { get; }

        ILinqResolverRegistry LinqResolvers { get; }

        IIndexInterceptorRegistry IndexInterceptors { get; }

        IServiceProvider Services { get; }

        ILogger Logger { get; }

        ConnectionString ConnectionString { get; }

        void RegisterQueryMetadata(string pluginId, int version, IReadOnlyCollection<string> reservedKeys);

        bool TryGetQueryMetadataDescriptor(string pluginId, out QueryMetadataDescriptor descriptor);

        QueryMetadataDescriptor GetQueryMetadataDescriptor(string pluginId);

        void RegisterBsonType(BsonTypeRegistration registration);

        bool TryGetBsonType(byte typeCode, out BsonTypeRegistration registration);

        bool TryGetBsonType(string name, out BsonTypeRegistration registration);

        void RegisterVectorIndexStrategy(VectorIndexStrategyDescriptor descriptor);

        bool TryGetVectorIndexStrategyDescriptor(string strategyId, out VectorIndexStrategyDescriptor descriptor);

        VectorIndexStrategyDescriptor GetVectorIndexStrategyDescriptor(string strategyId);

        void RegisterPageFactory(PageFactoryRegistration registration);

        bool TryGetPageFactory(string pageType, out PageFactoryRegistration registration);
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
    /// Represents a query planning rule that can participate in index selection.
    /// </summary>
    public interface IQueryPlanningRule
    {
        bool TryRewrite(QueryPlanningContext context);
    }

    /// <summary>
    /// Registry responsible for orchestrating <see cref="IQueryPlanningRule"/> implementations.
    /// </summary>
    public interface IQueryPlannerRegistry
    {
        void AddRule(IQueryPlanningRule rule, int order = 0);

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
    /// Delegate invoked for index interception during <c>ILiteCollection.EnsureIndex</c> execution.
    /// </summary>
    /// <param name="context">The interception context.</param>
    /// <returns>True when the interceptor handled the request and default processing should stop.</returns>
    public delegate bool IndexInterceptor(EnsureIndexContext context);

    /// <summary>
    /// Registry responsible for orchestrating index interceptors.
    /// </summary>
    public interface IIndexInterceptorRegistry
    {
        void Register(IndexInterceptor interceptor, int order = 0);

        IEnumerable<IndexInterceptor> Interceptors { get; }
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
