using System;
using LiteDB.Engine;
using LiteDB.Plugins.Bson;
using LiteDB.Plugins.Query;
using LiteDB.Plugins.Storage;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Represents the context provided to index interceptors.
    /// </summary>
    public sealed class EnsureIndexContext
    {
        private readonly Func<string, BsonExpression, bool, bool> _defaultHandler;
        private bool _defaultInvoked;
        private bool? _result;

        internal EnsureIndexContext(
            LiteDatabase database,
            LiteEngine engine,
            Type entityType,
            string collectionName,
            string name,
            BsonExpression expression,
            bool unique,
            BsonMapper mapper,
            ILitePluginContext pluginContext,
            Func<string, BsonExpression, bool, bool> defaultHandler)
        {
            Database = database;
            Engine = engine;
            EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
            CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Unique = unique;
            Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            PluginContext = pluginContext;
            _defaultHandler = defaultHandler ?? throw new ArgumentNullException(nameof(defaultHandler));
        }

        /// <summary>
        /// Gets the database associated with the collection.
        /// </summary>
        public LiteDatabase Database { get; }

        /// <summary>
        /// Gets the low-level engine for advanced operations.
        /// </summary>
        public LiteEngine Engine { get; }

        /// <summary>
        /// Gets the entity type associated with the collection.
        /// </summary>
        public Type EntityType { get; }

        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string CollectionName { get; }

        /// <summary>
        /// Gets or sets the index name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the expression used when creating the index.
        /// </summary>
        public BsonExpression Expression { get; }

        /// <summary>
        /// Gets a value indicating whether the index is unique.
        /// </summary>
        public bool Unique { get; }

        /// <summary>
        /// Gets the mapper associated with the collection.
        /// </summary>
        public BsonMapper Mapper { get; }

        /// <summary>
        /// Gets the plugin context for additional registry access.
        /// </summary>
        public ILitePluginContext PluginContext { get; }

        /// <summary>
        /// Gets the query metadata accessor exposed by the plugin context.
        /// </summary>
        public IQueryMetadataAccessor QueryMetadata => PluginContext?.QueryMetadata;

        /// <summary>
        /// Gets the BSON type registry exposed by the plugin context.
        /// </summary>
        public IBsonTypeRegistry BsonTypes => PluginContext?.BsonTypes;

        /// <summary>
        /// Gets the page factory registry exposed by the plugin context.
        /// </summary>
        public IPageFactoryRegistry PageFactories => PluginContext?.PageFactories;

        /// <summary>
        /// Gets the shared service provider available to plugins.
        /// </summary>
        public IServiceProvider Services => PluginContext?.Services;

        /// <summary>
        /// Gets a value indicating whether the default handler has been executed.
        /// </summary>
        public bool DefaultExecuted => _defaultInvoked;

        /// <summary>
        /// Gets the result assigned by the interceptor or default handler.
        /// </summary>
        public bool? Result => _result;

        /// <summary>
        /// Executes the default index creation logic using the current or overridden parameters.
        /// </summary>
        /// <param name="name">Optional replacement for the index name.</param>
        /// <param name="expression">Optional replacement expression.</param>
        /// <param name="unique">Optional replacement uniqueness flag.</param>
        /// <returns>The result produced by the default handler.</returns>
        public bool ExecuteDefault(string name = null, BsonExpression expression = null, bool? unique = null)
        {
            if (_defaultInvoked)
            {
                throw new InvalidOperationException("The default index handler has already been executed.");
            }

            var resolvedName = name ?? this.Name;
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                throw new ArgumentNullException(nameof(name), "Index name cannot be null or empty.");
            }

            var resolvedExpression = expression ?? this.Expression;
            if (resolvedExpression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            var resolvedUnique = unique ?? this.Unique;

            var result = _defaultHandler(resolvedName, resolvedExpression, resolvedUnique);

            _defaultInvoked = true;
            _result = result;

            return result;
        }

        /// <summary>
        /// Sets the final result for the interception.
        /// </summary>
        /// <param name="value">The result to assign.</param>
        public void SetResult(bool value)
        {
            _result = value;
        }
    }
}
