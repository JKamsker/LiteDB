using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins.Bson;
using LiteDB.Plugins.Indexing;
using LiteDB.Plugins.Query;
using LiteDB.Plugins.Storage;

namespace LiteDB.Plugins
{
    internal sealed class DefaultPluginContext : ILitePluginContext
    {
        public DefaultPluginContext(ConnectionString connectionString, IServiceProvider services, ILogger logger)
        {
            this.Expressions = new ExpressionRegistry();
            this.Indexes = new IndexRegistry();
            this.QueryPlanner = new QueryPlannerRegistry();
            this.QueryMetadata = new QueryMetadataAccessor();
            this.BsonTypes = new CustomBsonTypeRegistry();
            this.PageFactories = new PageTypeRegistry();
            this.CustomIndexes = new CustomIndexStrategyRegistry();
            this.LinqResolvers = new LinqResolverRegistry();
            this.IndexInterceptors = new IndexInterceptorRegistry();
            this.Services = services ?? NullServiceProvider.Instance;
            this.Logger = logger ?? NullLogger.Instance;
            this.ConnectionString = connectionString ?? new ConnectionString();
        }

        public IExpressionRegistry Expressions { get; }

        public IIndexRegistry Indexes { get; }

        public IQueryPlannerRegistry QueryPlanner { get; }

        public IQueryMetadataAccessor QueryMetadata { get; }

        public ICustomBsonTypeRegistry BsonTypes { get; }

        public IPageTypeRegistry PageFactories { get; }

        public ICustomIndexStrategyRegistry CustomIndexes { get; }

        public ILinqResolverRegistry LinqResolvers { get; }

        public IIndexInterceptorRegistry IndexInterceptors { get; }

        public IServiceProvider Services { get; }

        public ILogger Logger { get; }

        public ConnectionString ConnectionString { get; }

        public void RegisterQueryMetadata(string pluginId, int version, IReadOnlyCollection<string> reservedKeys)
        {
            this.QueryMetadata.Register(pluginId, version, reservedKeys);
        }

        public bool TryGetQueryMetadataDescriptor(string pluginId, out QueryMetadataDescriptor descriptor)
        {
            return this.QueryMetadata.TryGetDescriptor(pluginId, out descriptor);
        }

        public QueryMetadataDescriptor GetQueryMetadataDescriptor(string pluginId)
        {
            return this.QueryMetadata.GetDescriptor(pluginId);
        }

        public void RegisterBsonType(CustomBsonTypeDescriptor registration)
        {
            this.BsonTypes.Register(registration);
        }

        public bool TryGetBsonType(byte typeCode, out CustomBsonTypeDescriptor registration)
        {
            return this.BsonTypes.TryGetByTypeCode(typeCode, out registration);
        }

        public bool TryGetBsonType(string name, out CustomBsonTypeDescriptor registration)
        {
            return this.BsonTypes.TryGetByName(name, out registration);
        }

        public void RegisterPageFactory(PageFactoryRegistration registration)
        {
            this.PageFactories.Register(registration);
        }

        public bool TryGetPageFactory(string pageType, out PageFactoryRegistration registration)
        {
            return this.PageFactories.TryGet(pageType, out registration);
        }

        public void RegisterCustomIndexStrategy(CustomIndexStrategyDescriptor descriptor)
        {
            this.CustomIndexes.Register(descriptor);
        }

        public bool TryGetCustomIndexStrategyDescriptor(string strategyId, out CustomIndexStrategyDescriptor descriptor)
        {
            return this.CustomIndexes.TryGet(strategyId, out descriptor);
        }

        public CustomIndexStrategyDescriptor GetCustomIndexStrategyDescriptor(string strategyId)
        {
            return this.CustomIndexes.Get(strategyId);
        }
    }

    internal sealed class QueryMetadataAccessor : IQueryMetadataAccessor
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryMetadataDescriptor> _descriptors = new Dictionary<string, QueryMetadataDescriptor>(StringComparer.Ordinal);

        public void Register(string pluginId, int version, IReadOnlyCollection<string> reservedKeys)
        {
            var descriptor = new QueryMetadataDescriptor(pluginId, version, reservedKeys);

            lock (_sync)
            {
                _descriptors[descriptor.PluginId] = descriptor;
            }
        }

        public bool TryGetDescriptor(string pluginId, out QueryMetadataDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                descriptor = null;
                return false;
            }

            lock (_sync)
            {
                return _descriptors.TryGetValue(pluginId, out descriptor);
            }
        }

        public QueryMetadataDescriptor GetDescriptor(string pluginId)
        {
            if (!TryGetDescriptor(pluginId, out var descriptor))
            {
                throw new KeyNotFoundException($"No query metadata descriptor registered for plugin '{pluginId}'.");
            }

            return descriptor;
        }
    }

    internal sealed class CustomIndexStrategyRegistry : ICustomIndexStrategyRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, CustomIndexStrategyDescriptor> _strategies = new Dictionary<string, CustomIndexStrategyDescriptor>(StringComparer.Ordinal);

        public void Register(CustomIndexStrategyDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            lock (_sync)
            {
                _strategies[descriptor.StrategyId] = descriptor;
            }
        }

        public bool TryGet(string strategyId, out CustomIndexStrategyDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(strategyId))
            {
                descriptor = null;
                return false;
            }

            lock (_sync)
            {
                return _strategies.TryGetValue(strategyId, out descriptor);
            }
        }

        public CustomIndexStrategyDescriptor Get(string strategyId)
        {
            if (!TryGet(strategyId, out var descriptor))
            {
                throw new KeyNotFoundException($"No custom index strategy descriptor registered for '{strategyId}'.");
            }

            return descriptor;
        }

        public IReadOnlyCollection<CustomIndexStrategyDescriptor> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _strategies.Values.ToArray();
                }
            }
        }
    }

    internal sealed class ExpressionRegistry : IExpressionRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, BinaryOperatorRegistration> _operators = new Dictionary<string, BinaryOperatorRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExpressionFunctionRegistration> _functions = new Dictionary<string, ExpressionFunctionRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<BinaryOperatorRegistration> Operators
        {
            get
            {
                lock (_sync)
                {
                    return _operators.Values.ToArray();
                }
            }
        }

        public IReadOnlyCollection<ExpressionFunctionRegistration> Functions
        {
            get
            {
                lock (_sync)
                {
                    return _functions.Values.ToArray();
                }
            }
        }

        public IReadOnlyCollection<string> Keywords
        {
            get
            {
                lock (_sync)
                {
                    return _keywords.Keys.ToArray();
                }
            }
        }

        public void RegisterBinaryOperator(string token, BsonExpressionType expressionType, BsonBinaryOperator implementation, BinaryOperatorPrecedence precedence = BinaryOperatorPrecedence.Comparison, string source = null)
        {
            var registration = new BinaryOperatorRegistration(token, expressionType, implementation, precedence, source);

            lock (_sync)
            {
                _operators[registration.Token] = registration;
            }
        }

        public void RegisterFunction(string name, Delegate implementation, BsonExpressionType expressionType, bool convertScalarLeftToEnumerable = true, bool isScalarResult = false)
        {
            var registration = new ExpressionFunctionRegistration(name, implementation, expressionType, convertScalarLeftToEnumerable, isScalarResult);
            var key = GetFunctionKey(registration.Name, registration.AdditionalArgumentCount);

            lock (_sync)
            {
                _functions[key] = registration;
            }
        }

        public void RegisterKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) throw new ArgumentNullException(nameof(keyword));

            lock (_sync)
            {
                _keywords[keyword] = keyword;
            }
        }

        public bool ContainsOperator(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            lock (_sync)
            {
                return _operators.ContainsKey(token);
            }
        }

        public bool ContainsKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            lock (_sync)
            {
                return _keywords.ContainsKey(keyword);
            }
        }

        internal bool TryGetOperator(string token, out BinaryOperatorRegistration registration)
        {
            lock (_sync)
            {
                return _operators.TryGetValue(token, out registration);
            }
        }

        internal bool TryGetFunction(string name, int additionalArgumentCount, out ExpressionFunctionRegistration registration)
        {
            var key = GetFunctionKey(name, additionalArgumentCount);

            lock (_sync)
            {
                return _functions.TryGetValue(key, out registration);
            }
        }

        internal ExpressionFunctionRegistration FindByName(string name)
        {
            lock (_sync)
            {
                return _functions.Values.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static string GetFunctionKey(string name, int additionalArgumentCount)
        {
            return name.ToUpperInvariant() + "~" + additionalArgumentCount;
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

    internal sealed class LinqResolverRegistry : ILinqResolverRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<Type, LinqResolverFactory> _factories = new Dictionary<Type, LinqResolverFactory>();

        public void Register(Type targetType, LinqResolverFactory factory)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (_sync)
            {
                _factories[targetType] = factory;
            }
        }

        public bool TryGetFactory(Type targetType, out LinqResolverFactory factory)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));

            lock (_sync)
            {
                return _factories.TryGetValue(targetType, out factory);
            }
        }

        public IReadOnlyCollection<Type> RegisteredTypes
        {
            get
            {
                lock (_sync)
                {
                    return _factories.Keys.ToArray();
                }
            }
        }
    }

    internal sealed class IndexInterceptorRegistry : IIndexInterceptorRegistry
    {
        private readonly object _sync = new object();
        private readonly SortedList<int, List<IndexInterceptor>> _interceptors = new SortedList<int, List<IndexInterceptor>>();

        public void Register(IndexInterceptor interceptor, int order = 0)
        {
            if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));

            lock (_sync)
            {
                if (!_interceptors.TryGetValue(order, out var bucket))
                {
                    bucket = new List<IndexInterceptor>();
                    _interceptors.Add(order, bucket);
                }

                bucket.Add(interceptor);
            }
        }

        public IEnumerable<IndexInterceptor> Interceptors
        {
            get
            {
                lock (_sync)
                {
                    return _interceptors.Values.SelectMany(x => x).ToArray();
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








