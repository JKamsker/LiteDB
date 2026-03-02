using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteDB.Plugins.Bson;
using LiteDB.Plugins.Indexing;
using LiteDB.Plugins.Query;
using LiteDB.Plugins.Storage;

namespace LiteDB.Plugins
{
    internal sealed class DefaultPluginContext : ILitePluginContext, IPluginContextFreezeState
    {
        private int _frozen;
        private int _validationOnOpenRan;
        private int _validationOnOpenEngineInstanceId;
        private BsonDocument _validationOnOpenDiagnostics;
        private IPluginDiagnosticPolicy _diagnosticPolicy;

        public DefaultPluginContext(ConnectionString connectionString, IServiceProvider services, ILogger logger)
            : this(connectionString, services, logger, PluginMissingBehavior.RefuseDatabase, null)
        {
        }

        public DefaultPluginContext(ConnectionString connectionString, IServiceProvider services, ILogger logger, PluginMissingBehavior missingPluginBehavior, bool? validatePluginsOnOpen)
        {
            this.QueryOperators = new QueryOperatorRegistry(this);
            this.Expressions = new ExpressionRegistry(this.QueryOperators, this);
            this.Indexes = new IndexRegistry(this);
            this.QueryPlanner = new QueryPlannerRegistry(this);
            this.QueryMetadata = new QueryMetadataAccessor(this);
            this.DiagnosticPolicy = DefaultPluginDiagnosticPolicy.Instance;
            this.SqlFunctions = new SqlFunctionRegistry(this);
            this.QueryCostModels = new QueryCostModelRegistry(this);
            this.BsonTypes = new CustomBsonTypeRegistry(this);
            this.PageFactories = new PageTypeRegistry(this);
            this.IndexMetadata = new PluginIndexMetadataRegistry(this);
            this.CustomIndexes = new CustomIndexStrategyRegistry(this);
            this.LinqResolvers = new LinqResolverRegistry(this);
            this.Services = services ?? NullServiceProvider.Instance;
            this.Logger = logger ?? NullLogger.Instance;
            this.ConnectionString = connectionString ?? new ConnectionString();
            this.MissingPluginBehavior = PluginPolicyResolver.NormalizeMissingPluginBehavior(missingPluginBehavior);
            this.ValidatePluginsOnOpen = validatePluginsOnOpen;
        }

        public IExpressionRegistry Expressions { get; }

        public IIndexRegistry Indexes { get; }

        public IQueryPlannerRegistry QueryPlanner { get; }

        public IQueryMetadataAccessor QueryMetadata { get; }

        public IPluginDiagnosticPolicy DiagnosticPolicy
        {
            get => Volatile.Read(ref _diagnosticPolicy);
            private set => Volatile.Write(ref _diagnosticPolicy, value);
        }

        public ISqlFunctionRegistry SqlFunctions { get; }

        public IQueryOperatorRegistry QueryOperators { get; }

        public IQueryCostModelRegistry QueryCostModels { get; }

        public ICustomBsonTypeRegistry BsonTypes { get; }

        public IPageTypeRegistry PageFactories { get; }

        public IPluginIndexMetadataRegistry IndexMetadata { get; }

        public ICustomIndexStrategyRegistry CustomIndexes { get; }

        public ILinqResolverRegistry LinqResolvers { get; }

        public IServiceProvider Services { get; }

        public ILogger Logger { get; }

        public ConnectionString ConnectionString { get; }

        internal PluginMissingBehavior MissingPluginBehavior { get; }

        internal bool? ValidatePluginsOnOpen { get; }

        internal bool ValidationOnOpenRan => Volatile.Read(ref _validationOnOpenRan) != 0;

        internal int ValidationOnOpenEngineInstanceId => Volatile.Read(ref _validationOnOpenEngineInstanceId);

        internal BsonDocument ValidationOnOpenDiagnostics => Volatile.Read(ref _validationOnOpenDiagnostics);

        internal void RecordValidationOnOpen(BsonDocument diagnostics, int engineInstanceId)
        {
            Volatile.Write(ref _validationOnOpenDiagnostics, CloneDiagnostics(diagnostics));
            Volatile.Write(ref _validationOnOpenEngineInstanceId, engineInstanceId);
            Interlocked.Exchange(ref _validationOnOpenRan, 1);
        }

        internal static BsonDocument CloneDiagnostics(BsonDocument diagnostics)
        {
            if (diagnostics == null)
            {
                return null;
            }

            var clone = new BsonDocument();

            foreach (var element in diagnostics)
            {
                clone[element.Key] = CloneBsonValue(element.Value);
            }

            return clone;
        }

        private static BsonValue CloneBsonValue(BsonValue value)
        {
            if (value == null)
            {
                return null;
            }

            if (value.IsDocument)
            {
                var doc = new BsonDocument();

                foreach (var element in value.AsDocument)
                {
                    doc[element.Key] = CloneBsonValue(element.Value);
                }

                return doc;
            }

            if (value.IsArray)
            {
                var arr = new BsonArray();

                foreach (var item in value.AsArray)
                {
                    arr.Add(CloneBsonValue(item));
                }

                return arr;
            }

            if (value.IsBinary)
            {
                var bytes = value.AsBinary;
                return bytes == null ? BsonValue.Null : new BsonValue((byte[])bytes.Clone());
            }

            return value;
        }

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

        public void SetDiagnosticPolicy(IPluginDiagnosticPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            this.EnsureNotFrozen();

            DiagnosticPolicy = policy;
        }

        public void Freeze()
        {
            Interlocked.Exchange(ref _frozen, 1);
        }

        public bool IsFrozen => Volatile.Read(ref _frozen) != 0;

        public void EnsureNotFrozen()
        {
            if (this.IsFrozen)
            {
                throw new InvalidOperationException("Plugin context registries are frozen after initialization and cannot be modified.");
            }
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

        public void RegisterSqlFunction(SqlFunctionRegistration registration)
        {
            this.SqlFunctions.Register(registration);
        }

        public void RegisterQueryOperator(QueryOperatorRegistration registration)
        {
            this.QueryOperators.Register(registration);
        }

        public void RegisterQueryCostModel(QueryCostModelRegistration registration)
        {
            this.QueryCostModels.Register(registration);
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

        public void RegisterIndexMetadata(PluginIndexMetadataDescriptor descriptor)
        {
            this.IndexMetadata.Register(descriptor);
        }

        public bool TryGetIndexMetadataDescriptor(string indexKind, out PluginIndexMetadataDescriptor descriptor)
        {
            return this.IndexMetadata.TryGet(indexKind, out descriptor);
        }

        public PluginIndexMetadataDescriptor GetIndexMetadataDescriptor(string indexKind)
        {
            return this.IndexMetadata.Get(indexKind);
        }
    }

    internal sealed class QueryMetadataAccessor : IQueryMetadataAccessor
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryMetadataDescriptor> _descriptors = new Dictionary<string, QueryMetadataDescriptor>(StringComparer.Ordinal);

        public QueryMetadataAccessor(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

        public void Register(string pluginId, int version, IReadOnlyCollection<string> reservedKeys)
        {
            _freezeState.EnsureNotFrozen();

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
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, CustomIndexStrategyDescriptor> _strategies = new Dictionary<string, CustomIndexStrategyDescriptor>(StringComparer.Ordinal);

        public CustomIndexStrategyRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

        public void Register(CustomIndexStrategyDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            _freezeState.EnsureNotFrozen();

            lock (_sync)
            {
                if (_strategies.TryGetValue(descriptor.StrategyId, out var existing))
                {
                    throw new InvalidOperationException($"Index strategy '{descriptor.StrategyId}' is already registered by plugin '{existing.PluginId}'.");
                }

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

    internal sealed class PluginIndexMetadataRegistry : IPluginIndexMetadataRegistry
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, PluginIndexMetadataDescriptor> _descriptors = new Dictionary<string, PluginIndexMetadataDescriptor>(StringComparer.Ordinal);

        public PluginIndexMetadataRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

        public void Register(PluginIndexMetadataDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _freezeState.EnsureNotFrozen();

            lock (_sync)
            {
                if (_descriptors.TryGetValue(descriptor.IndexKind, out var existing))
                {
                    throw new InvalidOperationException($"Index metadata descriptor '{descriptor.IndexKind}' is already registered by plugin '{existing.PluginId}'.");
                }

                _descriptors[descriptor.IndexKind] = descriptor;
            }
        }

        public bool TryGet(string indexKind, out PluginIndexMetadataDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(indexKind))
            {
                descriptor = null;
                return false;
            }

            lock (_sync)
            {
                return _descriptors.TryGetValue(indexKind, out descriptor);
            }
        }

        public PluginIndexMetadataDescriptor Get(string indexKind)
        {
            if (!this.TryGet(indexKind, out var descriptor))
            {
                throw new KeyNotFoundException($"Index metadata descriptor '{indexKind}' was not registered.");
            }

            return descriptor;
        }

        public IReadOnlyCollection<PluginIndexMetadataDescriptor> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _descriptors.Values.ToArray();
                }
            }
        }
    }

    internal sealed class ExpressionRegistry : IExpressionRegistry
    {
        private readonly IQueryOperatorRegistry _queryOperators;
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, BinaryOperatorRegistration> _operators = new Dictionary<string, BinaryOperatorRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExpressionFunctionRegistration> _functions = new Dictionary<string, ExpressionFunctionRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ExpressionRegistry(IQueryOperatorRegistry queryOperators, IPluginContextFreezeState freezeState)
        {
            _queryOperators = queryOperators ?? throw new ArgumentNullException(nameof(queryOperators));
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

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

        public IQueryOperatorRegistry QueryOperators => _queryOperators;

        public void RegisterBinaryOperator(string token, BsonExpressionType expressionType, BsonBinaryOperator implementation, BinaryOperatorPrecedence precedence = BinaryOperatorPrecedence.Comparison, string source = null)
        {
            var registration = new BinaryOperatorRegistration(token, expressionType, implementation, precedence, source);

            _freezeState.EnsureNotFrozen();

            lock (_sync)
            {
                _operators[registration.Token] = registration;
            }
        }

        public void RegisterFunction(string name, Delegate implementation, BsonExpressionType expressionType, bool convertScalarLeftToEnumerable = true, bool isScalarResult = false)
        {
            var registration = new ExpressionFunctionRegistration(name, implementation, expressionType, convertScalarLeftToEnumerable, isScalarResult);
            var key = GetFunctionKey(registration.Name, registration.AdditionalArgumentCount);

            _freezeState.EnsureNotFrozen();

            lock (_sync)
            {
                _functions[key] = registration;
            }
        }

        public void RegisterKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) throw new ArgumentNullException(nameof(keyword));

            _freezeState.EnsureNotFrozen();

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
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, IIndexStrategy> _strategies = new Dictionary<string, IIndexStrategy>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<byte, IIndexStrategy> _strategiesByType = new Dictionary<byte, IIndexStrategy>();

        public IndexRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

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

            _freezeState.EnsureNotFrozen();

            if (strategy.IndexTypeCode == 0)
            {
                throw new InvalidOperationException("IndexTypeCode 0 is reserved for core indexes and cannot be registered by plugins.");
            }

            lock (_sync)
            {
                if (_strategies.TryGetValue(strategy.Kind, out var existingKind))
                {
                    throw new InvalidOperationException($"Index strategy kind '{strategy.Kind}' is already registered with type code {existingKind.IndexTypeCode}.");
                }

                if (_strategiesByType.TryGetValue(strategy.IndexTypeCode, out var existingType))
                {
                    throw new InvalidOperationException($"Index strategy type code {strategy.IndexTypeCode} is already registered by kind '{existingType.Kind}'.");
                }

                _strategies.Add(strategy.Kind, strategy);
                _strategiesByType.Add(strategy.IndexTypeCode, strategy);
            }
        }
    }

    internal sealed class QueryPlannerRegistry : IQueryPlannerRegistry
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly SortedList<int, List<IQueryPlanningRule>> _rules = new SortedList<int, List<IQueryPlanningRule>>();

        public QueryPlannerRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

        public void AddRule(IQueryPlanningRule rule, int order = 0)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            _freezeState.EnsureNotFrozen();

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
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<Type, LinqResolverFactory> _factories = new Dictionary<Type, LinqResolverFactory>();

        public LinqResolverRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState ?? throw new ArgumentNullException(nameof(freezeState));
        }

        public void Register(Type targetType, LinqResolverFactory factory)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _freezeState.EnsureNotFrozen();

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








