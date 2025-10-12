using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        private readonly Dictionary<string, OperatorRegistration> _operators = new Dictionary<string, OperatorRegistration>(StringComparer.OrdinalIgnoreCase);

        public void RegisterOperator(string token, string expression, BsonBinaryOperator operation, BsonExpressionType type, int precedence = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentNullException(nameof(token));
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentNullException(nameof(expression));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var method = operation.Method;

            if (method == null)
            {
                throw new ArgumentException("Operator delegates must expose a valid method.", nameof(operation));
            }

            if (!method.IsStatic)
            {
                throw new ArgumentException("Operator delegates must reference static methods.", nameof(operation));
            }

            var registration = new OperatorRegistration(expression, method, type, precedence);

            lock (_sync)
            {
                _operators[token] = registration;
                BsonExpressionParser.RegisterBinaryOperator(token, registration.Expression, registration.Method, registration.Type, registration.Precedence);
            }
        }

        private sealed class OperatorRegistration
        {
            public OperatorRegistration(string expression, MethodInfo method, BsonExpressionType type, int precedence)
            {
                this.Expression = expression;
                this.Method = method;
                this.Type = type;
                this.Precedence = precedence;
            }

            public string Expression { get; }

            public MethodInfo Method { get; }

            public BsonExpressionType Type { get; }

            public int Precedence { get; }
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
