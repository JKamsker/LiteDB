using System;
using System.Collections.Generic;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry for plugin-provided query planner cost models.
    /// Plugins can influence index selection by advertising costs for their custom index types.
    /// </summary>
    public interface IQueryCostModelRegistry
    {
        /// <summary>
        /// Registers a cost model for a custom index type.
        /// </summary>
        /// <param name="registration">The cost model registration.</param>
        /// <exception cref="ArgumentNullException">When registration is null.</exception>
        /// <exception cref="InvalidOperationException">When a cost model for the same index kind is already registered.</exception>
        void Register(QueryCostModelRegistration registration);

        /// <summary>
        /// Attempts to retrieve a cost model registration by index kind.
        /// </summary>
        /// <param name="indexKind">The index kind (e.g., "vector.hnsw").</param>
        /// <param name="registration">Receives the registration if found.</param>
        /// <returns>True if a cost model was found, false otherwise.</returns>
        bool TryGet(string indexKind, out QueryCostModelRegistration registration);

        /// <summary>
        /// Gets all registered cost models.
        /// </summary>
        IEnumerable<QueryCostModelRegistration> GetAll();
    }

    /// <summary>
    /// Describes a plugin-registered query cost model.
    /// </summary>
    public sealed class QueryCostModelRegistration
    {
        /// <summary>
        /// Initializes a new cost model registration.
        /// </summary>
        /// <param name="pluginId">The identifier of the owning plugin.</param>
        /// <param name="indexKind">The index kind this cost model applies to (e.g., "vector.hnsw").</param>
        /// <param name="calculateCost">The cost calculation delegate.</param>
        public QueryCostModelRegistration(
            string pluginId,
            string indexKind,
            Func<QueryCostContext, double> calculateCost)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("Plugin ID cannot be null or whitespace.", nameof(pluginId));

            if (string.IsNullOrWhiteSpace(indexKind))
                throw new ArgumentException("Index kind cannot be null or whitespace.", nameof(indexKind));

            PluginId = pluginId;
            IndexKind = indexKind;
            CalculateCost = calculateCost ?? throw new ArgumentNullException(nameof(calculateCost));
        }

        /// <summary>
        /// Gets the identifier of the plugin that registered this cost model.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the index kind this cost model applies to.
        /// </summary>
        public string IndexKind { get; }

        /// <summary>
        /// Gets the cost calculation delegate.
        /// </summary>
        public Func<QueryCostContext, double> CalculateCost { get; }
    }

    /// <summary>
    /// Context provided to cost calculation delegates.
    /// </summary>
    public sealed class QueryCostContext
    {
        /// <summary>
        /// Initializes a new query cost context.
        /// </summary>
        public QueryCostContext(
            string collectionName,
            string indexName,
            BsonExpression query,
            BsonDocument indexMetadata,
            long estimatedDocumentCount)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));

            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("Index name cannot be null or whitespace.", nameof(indexName));

            CollectionName = collectionName;
            IndexName = indexName;
            Query = query ?? throw new ArgumentNullException(nameof(query));
            IndexMetadata = indexMetadata ?? new BsonDocument();
            EstimatedDocumentCount = estimatedDocumentCount;
        }

        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string CollectionName { get; }

        /// <summary>
        /// Gets the index name.
        /// </summary>
        public string IndexName { get; }

        /// <summary>
        /// Gets the query expression.
        /// </summary>
        public BsonExpression Query { get; }

        /// <summary>
        /// Gets the index metadata.
        /// </summary>
        public BsonDocument IndexMetadata { get; }

        /// <summary>
        /// Gets the estimated number of documents in the collection.
        /// </summary>
        public long EstimatedDocumentCount { get; }
    }
}
