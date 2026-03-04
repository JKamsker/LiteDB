using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Query;
using LiteDB.Vector;
using LiteDB.Vector.Query;
using LiteDB.Vector.Utils;

namespace LiteDB.Vector.Tests.Infrastructure
{
    /// <summary>
    /// Provides a reusable in-memory database plus helpers for building vector indexes and running similarity queries.
    /// </summary>
    internal sealed class VectorTestContext : IDisposable
    {
        private readonly LiteDatabase _database;

        private VectorTestContext(LiteDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public LiteDatabase Database => _database;

        public static VectorTestContext Create(string? connectionString = null, IEnumerable<ILitePlugin>? plugins = null)
        {
            var pluginArray = plugins?.ToArray() ?? new[] { VectorSearchPlugin.Instance };
            var database = new LiteDatabase(connectionString ?? ":memory:", plugins: pluginArray);

            return new VectorTestContext(database);
        }

        public ILiteCollection<TDocument> GetCollection<TDocument>(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Collection name must be provided.", nameof(name));
            }

            return _database.GetCollection<TDocument>(name);
        }

        public ILiteCollection<TDocument> SeedCollection<TDocument>(
            string name,
            IEnumerable<TDocument>? documents,
            bool clearExisting = true)
        {
            var collection = GetCollection<TDocument>(name);

            if (clearExisting)
            {
                collection.DeleteAll();
            }

            if (documents != null)
            {
                collection.Insert(documents);
            }

            return collection;
        }

        public void EnsureVectorIndex<TDocument>(
            ILiteCollection<TDocument> collection,
            Expression<Func<TDocument, float[]>> field,
            VectorIndexOptions options,
            string? indexName = null)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var ensured = string.IsNullOrWhiteSpace(indexName)
                ? collection.EnsureIndex(field, options)
                : collection.EnsureIndex(indexName, field, options);

            if (!ensured)
            {
                throw new InvalidOperationException("Failed to ensure the requested vector index.");
            }
        }

        public void EnsureVectorIndex(
            ILiteCollection<BsonDocument> collection,
            string expression,
            VectorIndexOptions options,
            string? indexName = null,
            params BsonValue[] arguments)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("Expression must be provided.", nameof(expression));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var bsonExpression = CreateExpression(expression, arguments);

            var ensured = string.IsNullOrWhiteSpace(indexName)
                ? collection.EnsureIndex(bsonExpression, options)
                : collection.EnsureIndex(indexName, bsonExpression, options);

            if (!ensured)
            {
                throw new InvalidOperationException($"Failed to ensure vector index '{indexName ?? bsonExpression.Source}'.");
            }
        }

        public IReadOnlyList<TDocument> ExecuteWhereNear<TDocument>(
            ILiteCollection<TDocument> collection,
            Expression<Func<TDocument, float[]>> field,
            float[] target,
            double maxDistance,
            VectorDistanceMetric? metric = null,
            Action<ILiteQueryable<TDocument>>? configure = null)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var query = collection.Query().WhereNear(field, target, maxDistance, metric);
            configure?.Invoke(query);

            return query.ToArray();
        }

        public IReadOnlyList<TDocument> ExecuteTopKNear<TDocument>(
            ILiteCollection<TDocument> collection,
            Expression<Func<TDocument, float[]>> field,
            float[] target,
            int k,
            VectorDistanceMetric? metric = null,
            double? maxDistance = null)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return collection
                .Query()
                .TopKNear(field, target, k, metric, maxDistance)
                .ToArray();
        }

        public IReadOnlyList<TDocument> ExecuteMetadataSimilarity<TDocument>(
            ILiteCollection<TDocument> collection,
            Expression<Func<TDocument, float[]>> field,
            float[] target,
            double maxDistance,
            VectorDistanceMetric metric,
            Action<LiteQueryable<TDocument>>? configure = null)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var queryable = GetQueryable(collection);
            configure?.Invoke(queryable);

            ConfigureMetadata(queryable, field, target, maxDistance, metric);

            return queryable.ToArray();
        }

        public QueryMetadataBag ConfigureMetadata<TDocument>(
            LiteQueryable<TDocument> queryable,
            Expression<Func<TDocument, float[]>> field,
            float[] target,
            double maxDistance,
            VectorDistanceMetric metric)
        {
            if (queryable == null)
            {
                throw new ArgumentNullException(nameof(queryable));
            }

            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            var resolved = queryable.ResolveExpression(field);
            var fieldPath = resolved?.Source;

            if (string.IsNullOrWhiteSpace(fieldPath))
            {
                throw new InvalidOperationException("Unable to resolve the vector field expression for metadata configuration.");
            }

            return ConfigureMetadata(queryable.GetQueryDefinition(), fieldPath, target, maxDistance, metric);
        }

        public QueryMetadataBag ConfigureMetadata(
            LiteDB.Query query,
            string vectorField,
            float[] target,
            double maxDistance,
            VectorDistanceMetric metric)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var bag = query.GetOrCreateMetadata(
                VectorQueryMetadata.PluginId,
                () => new QueryMetadataBag(VectorQueryMetadata.PluginId, version: VectorQueryMetadata.Version, VectorQueryMetadata.ReservedKeys));

            bag.Set(VectorQueryMetadata.FieldKey, NormalizeVectorField(vectorField));
            bag.Set(VectorQueryMetadata.TargetKey, target);
            bag.Set(VectorQueryMetadata.MetricKey, (byte)metric);

            if (maxDistance < double.MaxValue)
            {
                var normalized = VectorEnsure.NormalizeMaxDistance(maxDistance, (byte)metric);
                bag.Set(VectorQueryMetadata.MaxDistanceKey, normalized);

                if (metric == VectorDistanceMetric.DotProduct)
                {
                    bag.Set(VectorQueryMetadata.MaxDistanceNormalizedKey, true);
                }
                else
                {
                    bag.Remove(VectorQueryMetadata.MaxDistanceNormalizedKey);
                }
            }
            else
            {
                bag.Remove(VectorQueryMetadata.MaxDistanceKey);
                bag.Remove(VectorQueryMetadata.MaxDistanceNormalizedKey);
            }

            return bag;
        }

        public BsonExpression CreateExpression(string expression)
        {
            return CreateExpression(expression, Array.Empty<BsonValue>());
        }

        public BsonExpression CreateExpression(string expression, params BsonValue[] arguments)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("Expression must be provided.", nameof(expression));
            }

            return arguments == null || arguments.Length == 0
                ? BsonExpression.Create(expression, _database.Services.ExpressionRegistry)
                : BsonExpression.Create(expression, _database.Services.ExpressionRegistry, arguments);
        }

        public LiteQueryable<TDocument> GetQueryable<TDocument>(ILiteCollection<TDocument> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            var query = collection.Query();

            if (query is LiteQueryable<TDocument> queryable)
            {
                return queryable;
            }

            throw new InvalidOperationException("The collection did not return the expected LiteQueryable instance.");
        }

        private static string NormalizeVectorField(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Vector field must be provided.", nameof(field));
            }

            var normalized = field.Trim();

            if (normalized.StartsWith("$", StringComparison.Ordinal))
            {
                return normalized;
            }

            if (normalized.StartsWith(".", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return "$." + normalized;
        }

        public void Dispose()
        {
            _database.Dispose();
        }
    }
}
