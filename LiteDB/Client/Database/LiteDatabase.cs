using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LiteDB.Engine;
using LiteDB.Plugins;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// The LiteDB database. Used for create a LiteDB instance and use all storage resources. It's the database connection
    /// </summary>
    public partial class LiteDatabase : ILiteDatabase
    {
        #region Properties

        private readonly ILiteEngine _engine;
        private readonly BsonMapper _mapper;
        private readonly bool _disposeOnClose;
        private readonly int? _checkpointOverride;
        private readonly DefaultPluginContext _pluginContext;

        /// <summary>
        /// Provides access to plugin services registered for this database instance.
        /// </summary>
        public LiteDatabaseServices Services { get; }

        /// <summary>
        /// Get current instance of BsonMapper used in this database instance (can be BsonMapper.Global)
        /// </summary>
        public BsonMapper Mapper => _mapper;

        #endregion

        #region Ctor

        /// <summary>
        /// Starts LiteDB database using a connection string for file system database
        /// </summary>
        public LiteDatabase(string connectionString, BsonMapper mapper = null, IEnumerable<ILitePlugin> plugins = null)
            : this(new ConnectionString(connectionString), mapper, plugins)
        {
        }

        /// <summary>
        /// Starts LiteDB database using a connection string for file system database
        /// </summary>
        public LiteDatabase(ConnectionString connectionString, BsonMapper mapper = null, IEnumerable<ILitePlugin> plugins = null)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            _engine = connectionString.CreateEngine();
            _disposeOnClose = true;

            var (resolvedMapper, resolvedPlugins, services, logger, missingPluginBehavior, validatePluginsOnOpen) = ResolveConfiguration(mapper, null, plugins);

            _mapper = resolvedMapper;
            _pluginContext = new DefaultPluginContext(connectionString, services, logger, missingPluginBehavior, validatePluginsOnOpen);
            this.Services = new LiteDatabaseServices(_pluginContext);

            this.InitializePluginsWithCleanup(resolvedPlugins);
        }

        /// <summary>
        /// Starts LiteDB database using a connection string and explicit options.
        /// </summary>
        public LiteDatabase(string connectionString, LiteDatabaseOptions options)
            : this(new ConnectionString(connectionString), options)
        {
        }

        /// <summary>
        /// Starts LiteDB database using a connection string and explicit options.
        /// </summary>
        public LiteDatabase(ConnectionString connectionString, LiteDatabaseOptions options)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            _engine = connectionString.CreateEngine();
            _disposeOnClose = true;

            var (resolvedMapper, resolvedPlugins, services, logger, missingPluginBehavior, validatePluginsOnOpen) = ResolveConfiguration(null, options, null);

            _mapper = resolvedMapper;
            _pluginContext = new DefaultPluginContext(connectionString, services, logger, missingPluginBehavior, validatePluginsOnOpen);
            this.Services = new LiteDatabaseServices(_pluginContext);

            this.InitializePluginsWithCleanup(resolvedPlugins);
        }

        /// <summary>
        /// Starts LiteDB database using a generic Stream implementation (mostly MemoryStream).
        /// </summary>
        /// <param name="stream">DataStream reference </param>
        /// <param name="mapper">BsonMapper mapper reference</param>
        /// <param name="logStream">LogStream reference </param>
        /// <param name="plugins">Optional plugins that will be initialized for this database instance.</param>
        public LiteDatabase(Stream stream, BsonMapper mapper = null, Stream logStream = null, IEnumerable<ILitePlugin> plugins = null)
        {
            var settings = new EngineSettings
            {
                DataStream = stream ?? throw new ArgumentNullException(nameof(stream)),
                LogStream = logStream
            };

            _engine = new LiteEngine(settings);
            _disposeOnClose = true;

            var (resolvedMapper, resolvedPlugins, services, logger, missingPluginBehavior, validatePluginsOnOpen) = ResolveConfiguration(mapper, null, plugins);

            _mapper = resolvedMapper;
            _pluginContext = new DefaultPluginContext(new ConnectionString(), services, logger, missingPluginBehavior, validatePluginsOnOpen);
            this.Services = new LiteDatabaseServices(_pluginContext);

            this.InitializePluginsWithCleanup(resolvedPlugins);

            if (logStream == null && stream is not MemoryStream)
            {
                if (!stream.CanWrite)
                {
                    // Read-only streams cannot participate in eager checkpointing because the process
                    // writes pages back to the underlying data stream immediately.
                }
                else
                {
                    // Without a dedicated log stream the WAL lives purely in memory; force
                    // checkpointing to ensure commits reach the underlying data stream.
                    var originalCheckpointSize = _engine.Pragma(Pragmas.CHECKPOINT);

                    if (originalCheckpointSize != 1)
                    {
                        _engine.Pragma(Pragmas.CHECKPOINT, 1);
                        _checkpointOverride = originalCheckpointSize;
                    }
                }
            }
        }

        /// <summary>
        /// Starts LiteDB database using a stream and explicit options.
        /// </summary>
        public LiteDatabase(Stream stream, LiteDatabaseOptions options)
            : this(stream, options, null)
        {
        }

        /// <summary>
        /// Starts LiteDB database using a stream, log stream, and explicit options.
        /// </summary>
        public LiteDatabase(Stream stream, LiteDatabaseOptions options, Stream logStream)
        {
            var settings = new EngineSettings
            {
                DataStream = stream ?? throw new ArgumentNullException(nameof(stream)),
                LogStream = logStream
            };

            _engine = new LiteEngine(settings);
            _disposeOnClose = true;

            var (resolvedMapper, resolvedPlugins, services, logger, missingPluginBehavior, validatePluginsOnOpen) = ResolveConfiguration(null, options, null);

            _mapper = resolvedMapper;
            _pluginContext = new DefaultPluginContext(new ConnectionString(), services, logger, missingPluginBehavior, validatePluginsOnOpen);
            this.Services = new LiteDatabaseServices(_pluginContext);

            this.InitializePluginsWithCleanup(resolvedPlugins);

            if (logStream == null && stream is not MemoryStream)
            {
                if (!stream.CanWrite)
                {
                    // Read-only streams cannot participate in eager checkpointing because the process
                    // writes pages back to the underlying data stream immediately.
                }
                else
                {
                    // Without a dedicated log stream the WAL lives purely in memory; force
                    // checkpointing to ensure commits reach the underlying data stream.
                    var originalCheckpointSize = _engine.Pragma(Pragmas.CHECKPOINT);

                    if (originalCheckpointSize != 1)
                    {
                        _engine.Pragma(Pragmas.CHECKPOINT, 1);
                        _checkpointOverride = originalCheckpointSize;
                    }
                }
            }
        }

        /// <summary>
        /// Start LiteDB database using a pre-exiting engine. When LiteDatabase instance dispose engine instance will be disposed too
        /// </summary>
        /// <param name="engine">Existing engine instance.</param>
        /// <param name="mapper">Optional mapper reference.</param>
        /// <param name="disposeOnClose">Indicates whether the database should dispose the engine when closed.</param>
        /// <param name="plugins">Optional plugins that will be initialized for this database instance.</param>
        public LiteDatabase(ILiteEngine engine, BsonMapper mapper = null, bool disposeOnClose = true, IEnumerable<ILitePlugin> plugins = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _disposeOnClose = disposeOnClose;

            var (resolvedMapper, resolvedPlugins, services, logger, missingPluginBehavior, validatePluginsOnOpen) = ResolveConfiguration(mapper, null, plugins);

            _mapper = resolvedMapper;
            _pluginContext = new DefaultPluginContext(new ConnectionString(), services, logger, missingPluginBehavior, validatePluginsOnOpen);
            this.Services = new LiteDatabaseServices(_pluginContext);

            this.InitializePluginsWithCleanup(resolvedPlugins);
        }

        /// <summary>
        /// Starts LiteDB database using an existing engine and explicit options.
        /// </summary>
        public LiteDatabase(ILiteEngine engine, LiteDatabaseOptions options)
            : this(engine, options, true)
        {
        }

        /// <summary>
        /// Starts LiteDB database using an existing engine, options, and custom disposal semantics.
        /// </summary>
        public LiteDatabase(ILiteEngine engine, LiteDatabaseOptions options, bool disposeOnClose)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _disposeOnClose = disposeOnClose;

            var (resolvedMapper, resolvedPlugins, services, logger, missingPluginBehavior, validatePluginsOnOpen) = ResolveConfiguration(null, options, null);

            _mapper = resolvedMapper;
            _pluginContext = new DefaultPluginContext(new ConnectionString(), services, logger, missingPluginBehavior, validatePluginsOnOpen);
            this.Services = new LiteDatabaseServices(_pluginContext);

            this.InitializePluginsWithCleanup(resolvedPlugins);
        }

        #endregion

        #region Collections

        /// <summary>
        /// Get a collection using an entity class as strong typed document. If collection does not exist, create a new one.
        /// </summary>
        /// <param name="name">Collection name (case insensitive)</param>
        /// <param name="autoId">Define autoId data type (when object contains no id field)</param>
        public ILiteCollection<T> GetCollection<T>(string name, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            return new LiteCollection<T>(name, autoId, _engine, _mapper, _pluginContext.Expressions, this, _pluginContext.LinqResolvers);
        }

        /// <summary>
        /// Get a collection using a name based on typeof(T).Name (BsonMapper.ResolveCollectionName function)
        /// </summary>
        public ILiteCollection<T> GetCollection<T>()
        {
            return this.GetCollection<T>(null);
        }

        /// <summary>
        /// Get a collection using a name based on typeof(T).Name (BsonMapper.ResolveCollectionName function)
        /// </summary>
        public ILiteCollection<T> GetCollection<T>(BsonAutoId autoId)
        {
            return this.GetCollection<T>(null, autoId);
        }

        /// <summary>
        /// Get a collection using a generic BsonDocument. If collection does not exist, create a new one.
        /// </summary>
        /// <param name="name">Collection name (case insensitive)</param>
        /// <param name="autoId">Define autoId data type (when document contains no _id field)</param>
        public ILiteCollection<BsonDocument> GetCollection(string name, BsonAutoId autoId = BsonAutoId.ObjectId)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            return new LiteCollection<BsonDocument>(name, autoId, _engine, _mapper, _pluginContext.Expressions, this, _pluginContext.LinqResolvers);
        }

        #endregion

        private static (BsonMapper Mapper, IEnumerable<ILitePlugin> Plugins, IServiceProvider Services, ILogger Logger, PluginMissingBehavior MissingPluginBehavior, bool? ValidatePluginsOnOpen) ResolveConfiguration(BsonMapper mapperOverride, LiteDatabaseOptions options, IEnumerable<ILitePlugin> legacyPlugins)
        {
            var mapper = mapperOverride ?? options?.Mapper ?? BsonMapper.Global;
            var plugins = options?.Plugins ?? legacyPlugins ?? Array.Empty<ILitePlugin>();
            var services = options?.Services ?? NullServiceProvider.Instance;
            var logger = options?.Logger ?? NullLogger.Instance;
            var missingPluginBehavior = PluginPolicyResolver.NormalizeMissingPluginBehavior(options?.MissingPluginBehavior ?? PluginMissingBehavior.RefuseDatabase);
            var validatePluginsOnOpen = options?.ValidatePluginsOnOpen;

            return (mapper, plugins, services, logger, missingPluginBehavior, validatePluginsOnOpen);
        }

        private void InitializePlugins(IEnumerable<ILitePlugin> plugins)
        {
            var initialized = new HashSet<Type>();

            if (plugins != null)
            {
                foreach (var plugin in plugins)
                {
                    if (plugin == null)
                    {
                        continue;
                    }

                    var type = plugin.GetType();

                    if (initialized.Add(type))
                    {
                        plugin.Initialize(this, _pluginContext);
                    }
                }
            }

            if (_engine is IPluginHost host)
            {
                host.SetPluginContext(_pluginContext);
            }
        }

        private void InitializePluginsWithCleanup(IEnumerable<ILitePlugin> plugins)
        {
            try
            {
                this.InitializePlugins(plugins);
            }
            catch
            {
                if (_disposeOnClose)
                {
                    try
                    {
                        _engine.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }

                throw;
            }
        }

        #region Transaction

        /// <summary>
        /// Initialize a new transaction. Transaction are created "per-thread". There is only one single transaction per thread.
        /// Return true if transaction was created or false if current thread already in a transaction.
        /// </summary>
        public bool BeginTrans() => _engine.BeginTrans();

        /// <summary>
        /// Commit current transaction
        /// </summary>
        public bool Commit() => _engine.Commit();

        /// <summary>
        /// Rollback current transaction
        /// </summary>
        public bool Rollback() => _engine.Rollback();

        #endregion

        #region FileStorage

        private ILiteStorage<string> _fs = null;

        /// <summary>
        /// Returns a special collection for storage files/stream inside datafile. Use _files and _chunks collection names. FileId is implemented as string. Use "GetStorage" for custom options
        /// </summary>
        public ILiteStorage<string> FileStorage
        {
            get { return _fs ?? (_fs = this.GetStorage<string>()); }
        }

        /// <summary>
        /// Get new instance of Storage using custom FileId type, custom "_files" collection name and custom "_chunks" collection. LiteDB support multiples file storages (using different files/chunks collection names)
        /// </summary>
        public ILiteStorage<TFileId> GetStorage<TFileId>(string filesCollection = "_files", string chunksCollection = "_chunks")
        {
            return new LiteStorage<TFileId>(this, filesCollection, chunksCollection);
        }

        #endregion

        #region Shortcut

        /// <summary>
        /// Get all collections name inside this database.
        /// </summary>
        public IEnumerable<string> GetCollectionNames()
        {
            // use $cols system collection with type = user only
            var cols = this.GetCollection("$cols")
                .Query()
                .Where("type = 'user'")
                .ToDocuments()
                .Select(x => x["name"].AsString)
                .ToArray();

            return cols;
        }

        /// <summary>
        /// Checks if a collection exists on database. Collection name is case insensitive
        /// </summary>
        public bool CollectionExists(string name)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            return this.GetCollectionNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Drop a collection and all data + indexes
        /// </summary>
        public bool DropCollection(string name)
        {
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            return _engine.DropCollection(name);
        }

        /// <summary>
        /// Rename a collection. Returns false if oldName does not exists or newName already exists
        /// </summary>
        public bool RenameCollection(string oldName, string newName)
        {
            if (oldName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(oldName));
            if (newName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(newName));

            return _engine.RenameCollection(oldName, newName);
        }

        #endregion

        #region Execute SQL

        /// <summary>
        /// Execute SQL commands and return as data reader.
        /// </summary>
        public IBsonDataReader Execute(TextReader commandReader, BsonDocument parameters = null)
        {
            if (commandReader == null) throw new ArgumentNullException(nameof(commandReader));

            var tokenizer = new Tokenizer(commandReader, _pluginContext.Expressions, _pluginContext.QueryOperators);
            var sql = new SqlParser(_engine, tokenizer, parameters);
            var reader = sql.Execute();

            return reader;
        }

        /// <summary>
        /// Execute SQL commands and return as data reader
        /// </summary>
        public IBsonDataReader Execute(string command, BsonDocument parameters = null)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var tokenizer = new Tokenizer(command, _pluginContext.Expressions, _pluginContext.QueryOperators);
            var sql = new SqlParser(_engine, tokenizer, parameters);
            var reader = sql.Execute();

            return reader;
        }

        /// <summary>
        /// Execute SQL commands and return as data reader
        /// </summary>
        public IBsonDataReader Execute(string command, params BsonValue[] args)
        {
            var p = new BsonDocument();
            var index = 0;

            foreach (var arg in args)
            {
                p[index.ToString()] = arg;
                index++;
            }

            return this.Execute(command, p);
        }

        #endregion

        #region Checkpoint/Rebuild

        /// <summary>
        /// Do database checkpoint. Copy all commited transaction from log file into datafile.
        /// </summary>
        public void Checkpoint()
        {
            _engine.Checkpoint();
        }

        /// <summary>
        /// Rebuild all database to remove unused pages - reduce data file
        /// </summary>
        public long Rebuild(RebuildOptions options = null)
        {
            return _engine.Rebuild(options ?? new RebuildOptions());
        }

        #endregion

        #region Pragmas

        /// <summary>
        /// Get value from internal engine variables
        /// </summary>
        public BsonValue Pragma(string name)
        {
            return _engine.Pragma(name);
        }

        /// <summary>
        /// Set new value to internal engine variables
        /// </summary>
        public BsonValue Pragma(string name, BsonValue value)
        {
            return _engine.Pragma(name, value);
        }

        /// <summary>
        /// Get/Set database user version - use this version number to control database change model
        /// </summary>
        public int UserVersion
        {
            get => _engine.Pragma(Pragmas.USER_VERSION);
            set => _engine.Pragma(Pragmas.USER_VERSION, value);
        }

        /// <summary>
        /// Get/Set database timeout - this timeout is used to wait for unlock using transactions
        /// </summary>
        public TimeSpan Timeout
        {
            get => TimeSpan.FromSeconds(_engine.Pragma(Pragmas.TIMEOUT).AsInt32);
            set => _engine.Pragma(Pragmas.TIMEOUT, (int)value.TotalSeconds);
        }

        /// <summary>
        /// Get/Set if database will deserialize dates in UTC timezone or Local timezone (default: Local)
        /// </summary>
        public bool UtcDate
        {
            get => _engine.Pragma(Pragmas.UTC_DATE);
            set => _engine.Pragma(Pragmas.UTC_DATE, value);
        }

        /// <summary>
        /// Get/Set database limit size (in bytes). New value must be equals or larger than current database size
        /// </summary>
        public long LimitSize
        {
            get => _engine.Pragma(Pragmas.LIMIT_SIZE);
            set => _engine.Pragma(Pragmas.LIMIT_SIZE, value);
        }

        /// <summary>
        /// Get/Set in how many pages (8 Kb each page) log file will auto checkpoint (copy from log file to data file). Use 0 to manual-only checkpoint (and no checkpoint on dispose)
        /// Default: 1000 pages
        /// </summary>
        public int CheckpointSize
        {
            get => _engine.Pragma(Pragmas.CHECKPOINT);
            set => _engine.Pragma(Pragmas.CHECKPOINT, value);
        }

        /// <summary>
        /// Get database collection (this options can be changed only in rebuild proces)
        /// </summary>
        public Collation Collation
        {
            get => new Collation(_engine.Pragma(Pragmas.COLLATION).AsString);
        }

        #endregion

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~LiteDatabase()
        {
            this.Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _disposeOnClose)
            {
                if (_checkpointOverride.HasValue)
                {
                    _engine.Pragma(Pragmas.CHECKPOINT, _checkpointOverride.Value);
                }

                _engine.Dispose();
            }
        }
    }
}
