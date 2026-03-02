using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins
{
    internal sealed class DefaultPluginContext : ILitePluginContext
    {
        public DefaultPluginContext(IServiceProvider services, ILogger logger)
        {
            this.Expressions = new ExpressionRegistry();
            this.Indexes = new IndexRegistry();
            this.QueryPlanner = new QueryPlannerRegistry();
            this.Services = services ?? NullServiceProvider.Instance;
            this.Logger = logger ?? NullLogger.Instance;
        }

        public IExpressionRegistry Expressions { get; }

        public IIndexRegistry Indexes { get; }

        public IQueryPlannerRegistry QueryPlanner { get; }

        public IServiceProvider Services { get; }

        public ILogger Logger { get; }
    }

    internal sealed class ExpressionRegistry : IExpressionRegistry
    {
        private readonly object _sync = new object();

        public void RegisterBinaryOperator(string name, BsonBinaryOperator operation, BsonExpressionType expressionType, int precedence = int.MaxValue, string source = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (operation.Target != null) throw new ArgumentException("Binary operators must be static methods.", nameof(operation));

            lock (_sync)
            {
                BsonExpressionParser.RegisterBinaryOperator(name, operation.Method, expressionType, precedence, source);
                Token.RegisterKeyword(name);
            }
        }

        public void RegisterFunction(string name, BsonExpressionFunction function, BsonExpressionType expressionType, bool convertScalarLeftToEnumerable = true, bool isScalarResult = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (function.Target != null) throw new ArgumentException("Expression functions must be static methods.", nameof(function));

            lock (_sync)
            {
                BsonExpressionParser.RegisterFunction(name, function.Method, expressionType, convertScalarLeftToEnumerable, isScalarResult);
            }
        }
    }

    internal sealed class IndexRegistry : IIndexRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, IIndexStrategy> _strategies = new Dictionary<string, IIndexStrategy>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<IIndexStrategy> AllFor(object collection)
        {
            lock (_sync)
            {
                return _strategies.Values.ToList();
            }
        }

        public IIndexStrategy GetByKind(string kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));

            lock (_sync)
            {
                _strategies.TryGetValue(kind, out var strategy);
                return strategy;
            }
        }

        public void Register(IIndexStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));

            lock (_sync)
            {
                _strategies[strategy.Kind] = strategy;
            }
        }
    }

    internal sealed class QueryPlannerRegistry : IQueryPlannerRegistry
    {
        private readonly object _sync = new object();
        private readonly SortedList<int, List<IQueryPlanningRule>> _rules = new SortedList<int, List<IQueryPlanningRule>>();

        public void AddRule(IQueryPlanningRule rule, int order = 0)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            lock (_sync)
            {
                if (!_rules.TryGetValue(order, out var bucket))
                {
                    bucket = new List<IQueryPlanningRule>();
                    _rules.Add(order, bucket);
                }

                bucket.Add(rule);
            }
        }

        public IEnumerable<IQueryPlanningRule> Rules
        {
            get
            {
                lock (_sync)
                {
                    return _rules.Values.SelectMany(x => x).ToArray();
                }
            }
        }
    }

    internal sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new NullServiceProvider();

        private NullServiceProvider()
        {
        }

        public object GetService(Type serviceType)
        {
            return null;
        }
    }

    internal sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();

        private NullLogger()
        {
        }

        public void Write(LogLevel level, string message, Exception exception = null)
        {
        }
    }
}
