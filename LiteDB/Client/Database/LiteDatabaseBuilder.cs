using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private enum DataSourceKind
        {
            None = 0,
            ConnectionString = 1,
            Stream = 2,
            Engine = 3
        }

        private readonly List<ILitePlugin> _plugins = new List<ILitePlugin>();
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

            _plugins.Add(plugin);
            return this;
        }

        public LiteDatabaseBuilder UsePlugins(IEnumerable<ILitePlugin> plugins)
        {
            if (plugins == null) throw new ArgumentNullException(nameof(plugins));

            foreach (var plugin in plugins)
            {
                if (plugin != null)
                {
                    _plugins.Add(plugin);
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
            var services = _options.Services;
            var logger = _options.Logger;
            var missingPluginBehavior = _options.MissingPluginBehavior;
            var validatePluginsOnOpen = _options.ValidatePluginsOnOpen;

            var plugins = new List<ILitePlugin>();
            if (_options.Plugins != null)
            {
                plugins.AddRange(_options.Plugins.Where(x => x != null));
            }

            plugins.AddRange(_plugins);

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

            var pluginContext = new DefaultPluginContext(contextConnectionString, services, logger, missingPluginBehavior, validatePluginsOnOpen);

            var database = new LiteDatabase(
                engine,
                disposeOnClose,
                mapper,
                pluginContext,
                initializePlugins: true,
                plugins: plugins,
                checkpointOverride: checkpointOverride);

            _built = true;

            return database;
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
