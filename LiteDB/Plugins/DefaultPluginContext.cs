using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LiteDB.Plugins
{
    internal sealed class DefaultPluginContext : ILitePluginContext
    {
        public DefaultPluginContext(ConnectionString connectionString, IServiceProvider services, ILogger logger)
        {
            this.ConnectionString = connectionString ?? new ConnectionString();
            this.Expressions = new ExpressionRegistry();
            this.Indexes = new IndexRegistry();
            this.QueryPlanner = new QueryPlannerRegistry();
            this.Services = services ?? NullServiceProvider.Instance;
            this.Logger = logger ?? NullLogger.Instance;
        }

        public ConnectionString ConnectionString { get; }

        public IExpressionRegistry Expressions { get; }

        public IIndexRegistry Indexes { get; }

        public IQueryPlannerRegistry QueryPlanner { get; }

        public IServiceProvider Services { get; }

        public ILogger Logger { get; }
    }

    internal sealed class ExpressionRegistry : IExpressionRegistry
    {
        private readonly object _sync = new object();
        private readonly List<BinaryOperatorRegistration> _operators = new List<BinaryOperatorRegistration>();
        private readonly Dictionary<string, MethodInfo> _functions = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void RegisterBinaryOperator(BinaryOperatorRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                _operators.Add(registration);
            }

            BsonExpressionParser.RegisterBinaryOperator(registration);
        }

        public void RegisterFunction(string name, Delegate implementation)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));

            var method = implementation.GetMethodInfo();

            if (!method.IsStatic)
            {
                throw new ArgumentException("Expression functions must reference static methods.", nameof(implementation));
            }

            var parameterCount = method.GetParameters().Length - 5;

            if (parameterCount < 0)
            {
                throw new ArgumentException("Expression functions must declare root, collation, parameters, and at least one argument slot.", nameof(implementation));
            }

            lock (_sync)
            {
                var key = ExpressionRegistryHelpers.CreateFunctionKey(name, parameterCount);
                _functions[key] = method;
            }

            BsonExpression.RegisterFunction(name, method, parameterCount);
        }

        public void RegisterKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) throw new ArgumentNullException(nameof(keyword));

            lock (_sync)
            {
                _keywords.Add(keyword);
            }

            Tokenizer.RegisterKeyword(keyword);
        }

        public ExpressionParserConfiguration CreateConfiguration()
        {
            lock (_sync)
            {
                return new ExpressionParserConfiguration(
                    _operators.ToArray(),
                    _functions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(_keywords, StringComparer.OrdinalIgnoreCase));
            }
        }
    }

    internal sealed class IndexRegistry : IIndexRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, IIndexStrategy> _strategies = new Dictionary<string, IIndexStrategy>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<byte, IIndexStrategy> _strategiesByType = new Dictionary<byte, IIndexStrategy>();

        public IEnumerable<IIndexStrategy> All
        {
            get
            {
                lock (_sync)
                {
                    return _strategies.Values.ToArray();
                }
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

        public IIndexStrategy GetByType(byte indexType)
        {
            lock (_sync)
            {
                _strategiesByType.TryGetValue(indexType, out var strategy);
                return strategy;
            }
        }

        public void Register(IIndexStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));

            lock (_sync)
            {
                _strategies[strategy.Kind] = strategy;
                _strategiesByType[strategy.IndexTypeCode] = strategy;
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
