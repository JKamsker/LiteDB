using System;
using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry for plugin-provided query cost models.
    /// </summary>
    public interface IQueryCostModelRegistry
    {
        void Register(QueryCostModelRegistration registration);

        IReadOnlyCollection<QueryCostModelRegistration> Registered { get; }
    }

    /// <summary>
    /// Describes a cost model with calculation logic for a plugin-owned index.
    /// </summary>
    public sealed class QueryCostModelRegistration
    {
        public QueryCostModelRegistration(
            string pluginId,
            string indexKind,
            Func<QueryCostContext, double> calculateCost)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(indexKind))
            {
                throw new ArgumentException("Index kind must be provided.", nameof(indexKind));
            }

            PluginId = pluginId;
            IndexKind = indexKind;
            CalculateCost = calculateCost ?? throw new ArgumentNullException(nameof(calculateCost));
        }

        public string PluginId { get; }

        public string IndexKind { get; }

        public Func<QueryCostContext, double> CalculateCost { get; }
    }

    /// <summary>
    /// Context passed to plugin cost models.
    /// </summary>
    public sealed class QueryCostContext
    {
        public QueryCostContext(
            string collectionName,
            string indexName,
            BsonExpression query,
            BsonDocument indexMetadata,
            long estimatedDocumentCount)
        {
            CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            IndexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
            Query = query ?? throw new ArgumentNullException(nameof(query));
            IndexMetadata = indexMetadata ?? throw new ArgumentNullException(nameof(indexMetadata));
            EstimatedDocumentCount = estimatedDocumentCount;
        }

        public string CollectionName { get; }

        public string IndexName { get; }

        public BsonExpression Query { get; }

        public BsonDocument IndexMetadata { get; }

        public long EstimatedDocumentCount { get; }
    }
}
