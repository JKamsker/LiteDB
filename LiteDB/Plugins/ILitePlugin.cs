using System;
using System.Collections.Generic;
using System.Reflection;
using LiteDB.Engine;

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
        ConnectionString ConnectionString { get; }

        IExpressionRegistry Expressions { get; }

        IIndexRegistry Indexes { get; }

        IQueryPlannerRegistry QueryPlanner { get; }

        IServiceProvider Services { get; }

        ILogger Logger { get; }
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
        void RegisterBinaryOperator(string name, string displayToken, MethodInfo method, BsonExpressionType expressionType, int precedence);

        void RegisterFunction(string name, MethodInfo method, int parameterCount, BsonExpressionType expressionType = BsonExpressionType.Call, bool convertScalarLeftToEnumerable = true, bool isScalarResult = false);

        void RegisterKeyword(string keyword);

        IEnumerable<ExpressionBinaryOperator> BinaryOperators { get; }

        IEnumerable<ExpressionFunction> Functions { get; }

        IEnumerable<string> Keywords { get; }
    }

    public readonly struct ExpressionBinaryOperator
    {
        public ExpressionBinaryOperator(string name, string displayToken, MethodInfo method, BsonExpressionType expressionType, int precedence)
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.DisplayToken = displayToken ?? throw new ArgumentNullException(nameof(displayToken));
            this.Method = method ?? throw new ArgumentNullException(nameof(method));
            this.ExpressionType = expressionType;
            this.Precedence = precedence;
        }

        public string Name { get; }

        public string DisplayToken { get; }

        public MethodInfo Method { get; }

        public BsonExpressionType ExpressionType { get; }

        public int Precedence { get; }
    }

    public readonly struct ExpressionFunction
    {
        public ExpressionFunction(string name, MethodInfo method, int parameterCount, BsonExpressionType expressionType, bool convertScalarLeftToEnumerable, bool isScalarResult)
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Method = method ?? throw new ArgumentNullException(nameof(method));
            this.ParameterCount = parameterCount;
            this.ExpressionType = expressionType;
            this.ConvertScalarLeftToEnumerable = convertScalarLeftToEnumerable;
            this.IsScalarResult = isScalarResult;
        }

        public string Name { get; }

        public MethodInfo Method { get; }

        public int ParameterCount { get; }

        public BsonExpressionType ExpressionType { get; }

        public bool ConvertScalarLeftToEnumerable { get; }

        public bool IsScalarResult { get; }
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
        bool TryRewrite(object context, out object plannedIndex);
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
