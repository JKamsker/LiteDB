using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LiteDB.Engine;
using LiteDB.Plugins;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// Fluent builder for creating <see cref="LiteDatabase"/> instances with explicit configuration and plugin registration.
    /// </summary>
    public sealed class LiteDatabaseBuilder
    {
        private sealed class PluginRegistration
        {
            public PluginRegistration(Type pluginType, Func<IServiceProvider, ILitePlugin> factory, bool ownsInstance)
            {
                PluginType = pluginType;
                Factory = factory ?? throw new ArgumentNullException(nameof(factory));
                OwnsInstance = ownsInstance;
            }

            public Type PluginType { get; }

            public Func<IServiceProvider, ILitePlugin> Factory { get; }

            public bool OwnsInstance { get; }
        }

        private sealed class DisposableCollection : IDisposable
        {
            private readonly List<IDisposable> _items = new List<IDisposable>();
            private int _disposed;

            public void Add(IDisposable item)
            {
                if (item == null) return;

                if (Volatile.Read(ref _disposed) != 0)
                {
                    item.Dispose();
                    return;
                }

                _items.Add(item);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                foreach (var item in _items)
                {
                    try
                    {
                        item.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }

                _items.Clear();
            }
        }

        private enum DataSourceKind
        {
            None = 0,
            ConnectionString = 1,
            Stream = 2,
            Engine = 3
        }

        private readonly List<PluginRegistration> _pluginRegistrations = new List<PluginRegistration>();
        private readonly LiteDatabaseOptions _options = new LiteDatabaseOptions();

        private DataSourceKind _dataSourceKind;
        private ConnectionString _connectionString;
        private Stream _dataStream;
        private Stream _logStream;
        private ILiteEngine _engine;
        private bool _ownsEngine;
        private ConnectionString _contextConnectionString;
        private Action<EngineSettings> _engineSettingsAction;
        private bool _built;

        public LiteDatabaseBuilder UsePlugin(ILitePlugin plugin)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));

            _pluginRegistrations.Add(new PluginRegistration(plugin.GetType(), _ => plugin, ownsInstance: false));
            return this;
        }

        public LiteDatabaseBuilder UsePlugin<TPlugin>()
            where TPlugin : ILitePlugin, new()
        {
            _pluginRegistrations.Add(new PluginRegistration(typeof(TPlugin), _ => new TPlugin(), ownsInstance: true));
            return this;
        }

        public LiteDatabaseBuilder UsePlugin(Func<ILitePlugin> pluginFactory)
        {
            if (pluginFactory == null) throw new ArgumentNullException(nameof(pluginFactory));

            _pluginRegistrations.Add(new PluginRegistration(pluginType: null, _ => pluginFactory(), ownsInstance: true));
            return this;
        }

        public LiteDatabaseBuilder UsePlugin(Func<IServiceProvider, ILitePlugin> pluginFactory)
        {
            if (pluginFactory == null) throw new ArgumentNullException(nameof(pluginFactory));

            _pluginRegistrations.Add(new PluginRegistration(pluginType: null, pluginFactory, ownsInstance: true));
            return this;
        }

        public LiteDatabaseBuilder UsePlugin<TPlugin>(Func<IServiceProvider, TPlugin> pluginFactory)
            where TPlugin : ILitePlugin
        {
            if (pluginFactory == null) throw new ArgumentNullException(nameof(pluginFactory));

            _pluginRegistrations.Add(new PluginRegistration(typeof(TPlugin), sp => pluginFactory(sp), ownsInstance: true));
            return this;
        }

        public LiteDatabaseBuilder UsePlugins(IEnumerable<ILitePlugin> plugins)
        {
            if (plugins == null) throw new ArgumentNullException(nameof(plugins));

            foreach (var plugin in plugins)
            {
                if (plugin != null)
                {
                    this.UsePlugin(plugin);
                }
            }

            return this;
        }

        public LiteDatabaseBuilder UsePlugins(params ILitePlugin[] plugins)
        {
            if (plugins == null) throw new ArgumentNullException(nameof(plugins));

            return this.UsePlugins((IEnumerable<ILitePlugin>)plugins);
        }

        public LiteDatabaseBuilder UseFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentNullException(nameof(filename));

            return this.UseConnectionString(new ConnectionString { Filename = filename });
        }

        public LiteDatabaseBuilder UseConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            return this.UseConnectionString(new ConnectionString(connectionString));
        }

        public LiteDatabaseBuilder UseConnectionString(ConnectionString connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            this.EnsureDataSourceNotSet();

            _dataSourceKind = DataSourceKind.ConnectionString;
            _connectionString = CopyConnectionString(connectionString);

            return this;
        }

        public LiteDatabaseBuilder UseInMemory()
        {
            return this.UseConnectionString(":memory:");
        }

        public LiteDatabaseBuilder UseTemp()
        {
            return this.UseConnectionString(":temp:");
        }

        public LiteDatabaseBuilder UseStream(Stream dataStream, Stream logStream = null)
        {
            if (dataStream == null) throw new ArgumentNullException(nameof(dataStream));

            this.EnsureDataSourceNotSet();

            _dataSourceKind = DataSourceKind.Stream;
            _dataStream = dataStream;
            _logStream = logStream;

            return this;
        }

        public LiteDatabaseBuilder UseEngine(ILiteEngine engine, bool ownsEngine = true)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            this.EnsureDataSourceNotSet();

            _dataSourceKind = DataSourceKind.Engine;
            _engine = engine;
            _ownsEngine = ownsEngine;

            return this;
        }

        public LiteDatabaseBuilder WithMapper(BsonMapper mapper)
        {
            _options.Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            return this;
        }

        public LiteDatabaseBuilder WithServices(IServiceProvider services)
        {
            _options.Services = services ?? throw new ArgumentNullException(nameof(services));
            return this;
        }

        public LiteDatabaseBuilder WithLogger(ILogger logger)
        {
            _options.Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            return this;
        }

        public LiteDatabaseBuilder WithMissingPluginBehavior(PluginMissingBehavior behavior)
        {
            _options.MissingPluginBehavior = behavior;
            return this;
        }

        public LiteDatabaseBuilder ValidatePluginsOnOpen(bool enabled = true)
        {
            _options.ValidatePluginsOnOpen = enabled;
            return this;
        }

        public LiteDatabaseBuilder WithContextConnectionString(ConnectionString connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            _contextConnectionString = CopyConnectionString(connectionString);
            return this;
        }

        public LiteDatabaseBuilder ConfigureEngine(Action<EngineSettings> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            _engineSettingsAction += configure;
            return this;
        }

        public LiteDatabaseBuilder ConfigureOptions(Action<LiteDatabaseOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            configure(_options);
            return this;
        }

        public ILiteDatabase Build()
        {
            this.EnsureNotBuilt();

            var mapper = _options.Mapper ?? BsonMapper.Global;
            var services = _options.Services ?? NullServiceProvider.Instance;
            var logger = _options.Logger ?? NullLogger.Instance;
            var missingPluginBehavior = _options.MissingPluginBehavior;
            var validatePluginsOnOpen = _options.ValidatePluginsOnOpen;

            var ownedResources = default(DisposableCollection);
            var plugins = default(List<ILitePlugin>);

            ILiteEngine engine;
            bool disposeOnClose;
            ConnectionString contextConnectionString;
            int? checkpointOverride = null;

            if (_dataSourceKind == DataSourceKind.ConnectionString)
            {
                contextConnectionString = _connectionString ?? throw new InvalidOperationException("Connection string must be configured before Build().");
                engine = contextConnectionString.CreateEngine(_engineSettingsAction);
                disposeOnClose = true;
            }
            else if (_dataSourceKind == DataSourceKind.Stream)
            {
                var settings = new EngineSettings
                {
                    DataStream = _dataStream,
                    LogStream = _logStream
                };

                _engineSettingsAction?.Invoke(settings);

                engine = new LiteEngine(settings);

                disposeOnClose = true;
                contextConnectionString = new ConnectionString();

                if (_logStream == null && _dataStream is not MemoryStream)
                {
                    if (_dataStream.CanWrite)
                    {
                        var originalCheckpointSize = engine.Pragma(Pragmas.CHECKPOINT);

                        if (originalCheckpointSize != 1)
                        {
                            engine.Pragma(Pragmas.CHECKPOINT, 1);
                            checkpointOverride = originalCheckpointSize;
                        }
                    }
                }
            }
            else if (_dataSourceKind == DataSourceKind.Engine)
            {
                engine = _engine;
                disposeOnClose = _ownsEngine;
                contextConnectionString = _contextConnectionString ?? new ConnectionString();
            }
            else
            {
                throw new InvalidOperationException("A data source must be configured before Build().");
            }

            try
            {
                (plugins, ownedResources) = this.CreatePluginInstances(services, logger);

                var pluginContext = new DefaultPluginContext(contextConnectionString, services, logger, missingPluginBehavior, validatePluginsOnOpen);

                var database = new LiteDatabase(
                    engine,
                    disposeOnClose,
                    mapper,
                    pluginContext,
                    initializePlugins: true,
                    plugins: plugins,
                    ownedResources: ownedResources,
                    checkpointOverride: checkpointOverride);

                _built = true;

                return database;
            }
            catch
            {
                ownedResources?.Dispose();
                throw;
            }
        }

        public ILiteDatabaseFactory BuildFactory()
        {
            this.EnsureNotBuilt();

            if (_dataSourceKind == DataSourceKind.Stream)
            {
                throw new InvalidOperationException("BuildFactory() is not supported for UseStream(...).");
            }

            var mapper = _options.Mapper ?? BsonMapper.Global;
            var services = _options.Services ?? NullServiceProvider.Instance;
            var logger = _options.Logger ?? NullLogger.Instance;
            var missingPluginBehavior = _options.MissingPluginBehavior;
            var validatePluginsOnOpen = _options.ValidatePluginsOnOpen;

            ILiteEngine engine;
            bool ownsEngine;
            ConnectionString contextConnectionString;

            if (_dataSourceKind == DataSourceKind.ConnectionString)
            {
                contextConnectionString = _connectionString ?? throw new InvalidOperationException("Connection string must be configured before BuildFactory().");
                engine = contextConnectionString.CreateEngine(_engineSettingsAction);
                ownsEngine = true;
            }
            else if (_dataSourceKind == DataSourceKind.Engine)
            {
                engine = _engine;
                ownsEngine = _ownsEngine;
                contextConnectionString = _contextConnectionString ?? new ConnectionString();
            }
            else
            {
                throw new InvalidOperationException("A data source must be configured before BuildFactory().");
            }

            DisposableCollection ownedResources = null;

            try
            {
                var (plugins, ownedPlugins) = this.CreatePluginInstances(services, logger);
                ownedResources = ownedPlugins;

                var pluginContext = new DefaultPluginContext(contextConnectionString, services, logger, missingPluginBehavior, validatePluginsOnOpen);

                var factory = new LiteDatabaseFactory(
                    engine,
                    ownsEngine,
                    mapper,
                    pluginContext,
                    plugins,
                    ownedResources);

                _built = true;

                return factory;
            }
            catch
            {
                ownedResources?.Dispose();

                if (ownsEngine)
                {
                    try
                    {
                        engine?.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }

                throw;
            }
        }

        private (List<ILitePlugin> Plugins, DisposableCollection OwnedResources) CreatePluginInstances(IServiceProvider services, ILogger logger)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var ownedResources = new DisposableCollection();
            var plugins = new List<ILitePlugin>();
            var seen = new HashSet<Type>();

            var registrations = new List<PluginRegistration>();

            if (_options.Plugins != null)
            {
                foreach (var plugin in _options.Plugins)
                {
                    if (plugin != null)
                    {
                        registrations.Add(new PluginRegistration(plugin.GetType(), _ => plugin, ownsInstance: false));
                    }
                }
            }

            registrations.AddRange(_pluginRegistrations);

            foreach (var registration in registrations)
            {
                if (registration == null)
                {
                    continue;
                }

                if (registration.PluginType != null && seen.Contains(registration.PluginType))
                {
                    logger.Write(LogLevel.Warning, $"Plugin '{registration.PluginType.FullName}' was registered multiple times. Using first instance and ignoring subsequent registrations.");
                    continue;
                }

                var plugin = registration.Factory(services);
                if (plugin == null)
                {
                    continue;
                }

                var pluginType = plugin.GetType();

                if (seen.Contains(pluginType))
                {
                    logger.Write(LogLevel.Warning, $"Plugin '{pluginType.FullName}' was registered multiple times. Using first instance and ignoring subsequent registrations.");

                    if (registration.OwnsInstance && plugin is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }

                    continue;
                }

                seen.Add(pluginType);
                plugins.Add(plugin);

                if (registration.OwnsInstance && plugin is IDisposable ownedDisposable)
                {
                    ownedResources.Add(ownedDisposable);
                }
            }

            return (plugins, ownedResources);
        }

        private void EnsureNotBuilt()
        {
            if (_built)
            {
                throw new InvalidOperationException("Builder is single-use. Create a new LiteDatabaseBuilder instance.");
            }
        }

        private void EnsureDataSourceNotSet()
        {
            if (_dataSourceKind != DataSourceKind.None)
            {
                throw new InvalidOperationException("Data source is already configured.");
            }
        }

        private static ConnectionString CopyConnectionString(ConnectionString source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new ConnectionString
            {
                Connection = source.Connection,
                Filename = source.Filename,
                Password = source.Password,
                InitialSize = source.InitialSize,
                ReadOnly = source.ReadOnly,
                Upgrade = source.Upgrade,
                AutoRebuild = source.AutoRebuild,
                Collation = source.Collation
            };
        }
    }
}
