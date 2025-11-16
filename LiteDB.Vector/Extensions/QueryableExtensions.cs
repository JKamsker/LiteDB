using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LiteDB;
using LiteDB.Vector.Utils;
using LiteDB.Plugins.Query;
using LiteDB.Vector.Query;

namespace LiteDB.Vector.Extensions
{
    internal static class QueryableExtensions
    {

        internal static ILiteQueryable<T> WhereNear<T>(LiteQueryable<T> source, string vectorField, float[] target, double maxDistance, VectorDistanceMetric? metric)
        {
            if (string.IsNullOrWhiteSpace(vectorField))
            {
                throw new ArgumentNullException(nameof(vectorField));
            }

            var fieldExpr = BsonExpression.Create($"$.{vectorField}", source.ExpressionRegistry);
            return WhereNear(source, fieldExpr, target, maxDistance, metric);
        }

        internal static ILiteQueryable<T> WhereNear<T, K>(LiteQueryable<T> source, Expression<Func<T, K>> field, float[] target, double maxDistance, VectorDistanceMetric? metric)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            var fieldExpr = source.ResolveExpression(field);
            return WhereNear(source, fieldExpr, target, maxDistance, metric);
        }

        internal static ILiteQueryable<T> WhereNear<T>(LiteQueryable<T> source, BsonExpression fieldExpr, float[] target, double maxDistance, VectorDistanceMetric? metric)
        {
            EnsureVectorPluginAvailable(source);

            if (fieldExpr == null)
            {
                throw new ArgumentNullException(nameof(fieldExpr));
            }

            ValidateVectorArguments(target, maxDistance);

            var existingMetric = GetQueryMetric(source);
            var metricByte = metric.HasValue ? (byte)metric.Value : existingMetric ?? ResolveMetricFromIndexes(source, fieldExpr);
            var adjustedMaxDistance = VectorEnsure.NormalizeMaxDistance(maxDistance, metricByte);

            var filter = CreateVectorDistanceFilter(source, fieldExpr, target, adjustedMaxDistance, metricByte);
            source.Where(filter);

            ConfigureVectorMetadata(source, fieldExpr, target, maxDistance, metricByte);

            return source;
        }

        internal static ILiteQueryableResult<T> TopKNear<T>(LiteQueryable<T> source, BsonExpression fieldExpr, float[] target, int k, VectorDistanceMetric? metric, double? maxDistance)
        {
            EnsureVectorPluginAvailable(source);

            if (fieldExpr == null)
            {
                throw new ArgumentNullException(nameof(fieldExpr));
            }

            if (target == null || target.Length == 0)
            {
                throw new ArgumentException("Target vector must be provided.", nameof(target));
            }

            if (k <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(k), "Top-K must be greater than zero.");
            }

            var effectiveMaxDistance = maxDistance ?? double.MaxValue;
            var existingMetric = GetQueryMetric(source);
            var metricByte = metric.HasValue ? (byte)metric.Value : existingMetric;

            if (maxDistance.HasValue)
            {
                WhereNear(source, fieldExpr, target, effectiveMaxDistance, metric);
            }
            else
            {
                ConfigureVectorMetadata(source, fieldExpr, target, effectiveMaxDistance, metricByte);
            }

            var distanceExpression = CreateVectorDistanceExpression(source, fieldExpr, target, metricByte);

            return source
                .OrderBy(distanceExpression, global::LiteDB.Query.Ascending)
                .ThenBy(BsonExpression.Create("$._id", source.ExpressionRegistry))
                .Limit(k);
        }

        internal static ILiteQueryable<T> OrderByNearest<T>(LiteQueryable<T> source, BsonExpression fieldExpr, float[] target, VectorDistanceMetric? metric, double? maxDistance)
        {
            EnsureVectorPluginAvailable(source);

            if (fieldExpr == null)
            {
                throw new ArgumentNullException(nameof(fieldExpr));
            }

            if (target == null || target.Length == 0)
            {
                throw new ArgumentException("Target vector must be provided.", nameof(target));
            }

            var effectiveMaxDistance = maxDistance ?? double.MaxValue;
            var existingMetric = GetQueryMetric(source);
            var metricByte = metric.HasValue ? (byte)metric.Value : existingMetric;

            if (maxDistance.HasValue)
            {
                WhereNear(source, fieldExpr, target, effectiveMaxDistance, metric);
            }
            else
            {
                ConfigureVectorMetadata(source, fieldExpr, target, effectiveMaxDistance, metricByte);
            }

            var distanceExpression = CreateVectorDistanceExpression(source, fieldExpr, target, metricByte);

            return source
                .OrderBy(distanceExpression, global::LiteDB.Query.Ascending)
                .ThenBy(BsonExpression.Create("$._id", source.ExpressionRegistry));
        }

        private static void EnsureVectorPluginAvailable<T>(LiteQueryable<T> source)
        {
            var services = source.Database?.Services;

            if (VectorCompatibility.TryGetStrategy(services?.CustomIndexes) == null)
            {
                throw VectorCompatibility.PluginRequired();
            }
        }

        private static void ValidateVectorArguments(float[] target, double maxDistance)
        {
            if (target == null || target.Length == 0)
            {
                throw new ArgumentException("Target vector must be provided.", nameof(target));
            }

            if (double.IsNaN(maxDistance))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistance), "Similarity threshold must be a valid number.");
            }
        }

        private static byte? GetQueryMetric<T>(LiteQueryable<T> source)
        {
            var metadata = TryGetExistingMetadata(source);

            if (metadata != null && metadata.TryGet<byte?>(VectorQueryMetadata.MetricKey, out var metric))
            {
                return metric;
            }

            return null;
        }

        private static QueryMetadataBag? TryGetExistingMetadata<T>(LiteQueryable<T> source)
        {
            var query = source.GetQueryDefinition();
            return query.TryGetMetadata(VectorQueryMetadata.PluginId, out var metadata) ? metadata : null;
        }

        private static QueryMetadataBag GetOrCreateMetadata<T>(LiteQueryable<T> source)
        {
            var services = source.Database?.Services;
            var accessor = services?.QueryMetadata;

            if (accessor != null && accessor.TryGetDescriptor(VectorQueryMetadata.PluginId, out var descriptor))
            {
                return source.GetOrCreateMetadata(VectorQueryMetadata.PluginId, () => new QueryMetadataBag(descriptor));
            }

            return source.GetOrCreateMetadata(
                VectorQueryMetadata.PluginId,
                () => new QueryMetadataBag(VectorQueryMetadata.PluginId, version: VectorQueryMetadata.Version, VectorQueryMetadata.ReservedKeys));
        }

        private static void ConfigureVectorMetadata<T>(LiteQueryable<T> source, BsonExpression fieldExpr, float[] target, double maxDistance, byte? metric)
        {
            var metadata = GetOrCreateMetadata(source);

            metadata.Set(VectorQueryMetadata.FieldKey, fieldExpr.Source);
            metadata.Set(VectorQueryMetadata.TargetKey, target?.ToArray());
            metadata.Set(VectorQueryMetadata.MetricKey, metric);

            if (maxDistance < double.MaxValue)
            {
                var normalized = VectorEnsure.NormalizeMaxDistance(maxDistance, metric);
                metadata.Set(VectorQueryMetadata.MaxDistanceKey, normalized);
            }
            else
            {
                metadata.Remove(VectorQueryMetadata.MaxDistanceKey);
            }
        }

        private static BsonExpression CreateVectorDistanceFilter<T>(LiteQueryable<T> source, BsonExpression fieldExpr, float[] target, double maxDistance, byte? metric)
        {
            var parameters = new List<BsonValue>
            {
                new BsonArray(target.Select(value => new BsonValue(value))),
                new BsonValue(maxDistance)
            };

            var metricPlaceholder = string.Empty;

            if (metric.HasValue)
            {
                parameters.Add(new BsonValue(metric.Value));
                metricPlaceholder = ", @2";
            }

            var vectorExpression = $"VECTOR_DIST({fieldExpr.Source}, @0{metricPlaceholder})";

            return BsonExpression.Create($"({vectorExpression}) <= @1", source.ExpressionRegistry, parameters.ToArray());
        }

        private static BsonExpression CreateVectorDistanceExpression<T>(LiteQueryable<T> source, BsonExpression fieldExpr, float[] target, byte? metric)
        {
            var parameters = new List<BsonValue>
            {
                new BsonArray(target.Select(value => new BsonValue(value)))
            };

            var metricPlaceholder = string.Empty;

            if (metric.HasValue)
            {
                parameters.Add(new BsonValue(metric.Value));
                metricPlaceholder = ", @1";
            }

            return BsonExpression.Create($"VECTOR_DIST({fieldExpr.Source}, @0{metricPlaceholder})", source.ExpressionRegistry, parameters.ToArray());
        }

        private static byte? ResolveMetricFromIndexes<T>(LiteQueryable<T> source, BsonExpression fieldExpr)
        {
            var database = source.Database;
            var collection = source.CollectionName;

            if (database == null || string.IsNullOrWhiteSpace(collection) || fieldExpr == null)
            {
                return null;
            }

            try
            {
                var indexes = database.GetCollection("$indexes");

                if (indexes == null)
                {
                    return null;
                }

                var filter = global::LiteDB.Query.And(
                    global::LiteDB.Query.EQ("collection", collection),
                    global::LiteDB.Query.EQ("expression", fieldExpr.Source),
                    global::LiteDB.Query.EQ("type", "vector"));

                var descriptor = indexes.FindOne(filter);

                if (descriptor != null &&
                    descriptor.TryGetValue("metric", out var metricValue) &&
                    metricValue.IsNumber)
                {
                    var resolved = (byte)metricValue.AsInt32;

                    if ((VectorDistanceMetric)resolved == VectorDistanceMetric.DotProduct)
                    {
                        return resolved;
                    }
                }
            }
            catch
            {
                // Ignore system collection failures and fall back to caller-provided metric.
            }

            return null;
        }

    }
}




