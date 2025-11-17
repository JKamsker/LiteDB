using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Vector.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Query;
using LiteDB.Vector.Query;
using LiteDB.Vector.Utils;

namespace LiteDB.Vector.Query
{
    /// <summary>
    /// Query planning rule responsible for selecting vector index usage when the plugin is enabled.
    /// </summary>
    internal sealed class VectorIndexPlanningRule : IQueryPlanningRule
    {
        public bool TryRewrite(QueryPlanningContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var snapshot = context.Snapshot;
            var collection = snapshot?.CollectionPage;

            if (snapshot == null || collection == null)
            {
                return false;
            }

            var collation = snapshot.Collation;

            string? expression = null;
            float[]? target = null;
            double maxDistance = double.MaxValue;
            byte? metric = null;
            BsonExpression? consumedTerm = null;
            var matchedFromOrderBy = false;
            QueryMetadataBag? metadataBag = null;
            var maxDistanceNormalized = false;

            if (context.Query.TryGetMetadata(VectorQueryMetadata.PluginId, out var bag))
            {
                metadataBag = bag;
            }

            foreach (var term in context.Terms)
            {
                if (TryParseVectorPredicate(term, collation, out expression, out target, out maxDistance))
                {
                    consumedTerm = term;
                    maxDistanceNormalized = true;
                    break;
                }
            }

            if (expression == null && context.Query.OrderBy.Count > 0)
            {
                foreach (var order in context.Query.OrderBy)
                {
                    if (TryParseVectorExpression(order.Expression, collation, out expression, out target))
                    {
                        matchedFromOrderBy = true;
                        maxDistance = double.MaxValue;
                        break;
                    }
                }
            }

            if (metadataBag != null)
            {
                if (expression == null)
                {
                    if (metadataBag.TryGet<string>(VectorQueryMetadata.FieldKey, out var field) &&
                        !string.IsNullOrWhiteSpace(field) &&
                        metadataBag.TryGet<float[]>(VectorQueryMetadata.TargetKey, out var bagTarget) &&
                        bagTarget != null)
                    {
                        expression = NormalizeVectorField(field);
                        target = bagTarget;

                        if (metadataBag.TryGet<double>(VectorQueryMetadata.MaxDistanceKey, out var bagDistance))
                        {
                            maxDistance = bagDistance;
                            maxDistanceNormalized = metadataBag.Version >= VectorQueryMetadata.Version;
                        }

                        if (metadataBag.TryGet<byte?>(VectorQueryMetadata.MetricKey, out var bagMetric))
                        {
                            metric = bagMetric;
                        }

                        matchedFromOrderBy = matchedFromOrderBy ||
                            context.Query.OrderBy.Any(order =>
                                IsVectorDistance(order.Expression) ||
                                IsVectorSimilarity(order.Expression));
                    }
                }
                else if (metadataBag.TryGet<string>(VectorQueryMetadata.FieldKey, out var field) &&
                    !string.IsNullOrWhiteSpace(field))
                {
                    var normalizedField = NormalizeVectorField(field);

                    if (string.Equals(normalizedField, expression, StringComparison.OrdinalIgnoreCase))
                    {
                        if (target == null &&
                            metadataBag.TryGet<float[]>(VectorQueryMetadata.TargetKey, out var bagTarget) &&
                            bagTarget != null)
                        {
                            target = bagTarget;
                        }

                        if (metadataBag.TryGet<double>(VectorQueryMetadata.MaxDistanceKey, out var bagDistance))
                        {
                            maxDistance = bagDistance;
                            maxDistanceNormalized = metadataBag.Version >= VectorQueryMetadata.Version;
                        }

                        if (!metric.HasValue &&
                            metadataBag.TryGet<byte?>(VectorQueryMetadata.MetricKey, out var bagMetric))
                        {
                            metric = bagMetric;
                        }
                    }
                }
            }


#pragma warning disable CS0618
            if (!metric.HasValue && context.Query.VectorMetric.HasValue)
            {
                metric = context.Query.VectorMetric;
            }
#pragma warning restore CS0618

#pragma warning disable CS0618
            if (expression == null && context.Query.VectorTarget != null && context.Query.VectorField != null)
            {
                expression = NormalizeVectorField(context.Query.VectorField);
                target = context.Query.VectorTarget?.ToArray();
                maxDistance = context.Query.VectorMaxDistance;
                maxDistanceNormalized = true;
                matchedFromOrderBy = matchedFromOrderBy ||
                    context.Query.OrderBy.Any(order =>
                        IsVectorDistance(order.Expression) ||
                        IsVectorSimilarity(order.Expression));
            }
#pragma warning restore CS0618

            if (expression == null || target == null)
            {
                return false;
            }

            int? limit = context.Query.Limit != int.MaxValue ? context.Query.Limit : (int?)null;

            foreach (var (index, pluginId, metadataBuffer) in collection.GetPluginIndexes())
            {
                if (!string.Equals(pluginId, ReservedCodeRanges.VectorPluginId, StringComparison.Ordinal))
                {
                    continue;
                }

                var metadata = VectorIndexMetadata.Wrap(metadataBuffer);

                if (!string.Equals(index.Expression, expression, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (metadata.Dimensions != target.Length)
                {
                    continue;
                }

                byte? metricByte = metric ?? metadata.Metric;
                var effectiveMaxDistance = VectorEnsure.NormalizeMaxDistance(maxDistance, metricByte, maxDistanceNormalized);

                var vectorIndex = new VectorIndexQuery(index.Name, snapshot, index, metadata, target, effectiveMaxDistance, limit, collation);
                var consumed = consumedTerm != null ? new[] { consumedTerm } : Array.Empty<BsonExpression>();

                context.UseIndex(
                    vectorIndex,
                    index.Expression,
                    consumed,
                    isIndexKeyOnly: false,
                    indexCost: vectorIndex.GetCost(index));

                context.VectorOrderConsumed = matchedFromOrderBy;
                return true;
            }

            return false;
        }

        private static bool TryParseVectorPredicate(BsonExpression? predicate, Collation collation, out string? expression, out float[]? target, out double maxDistance)
        {
            expression = null;
            target = null;
            maxDistance = double.MaxValue;

            if (predicate == null)
            {
                return false;
            }

            if ((predicate.Type == BsonExpressionType.LessThan || predicate.Type == BsonExpressionType.LessThanOrEqual) &&
                TryParseVectorExpression(predicate.Left, collation, out expression, out target) &&
                TryConvertToDouble(predicate.Right?.ExecuteScalar(collation), out maxDistance))
            {
                return true;
            }

            if ((predicate.Type == BsonExpressionType.GreaterThan || predicate.Type == BsonExpressionType.GreaterThanOrEqual) &&
                TryParseVectorExpression(predicate.Right, collation, out expression, out target) &&
                TryConvertToDouble(predicate.Left?.ExecuteScalar(collation), out maxDistance))
            {
                return true;
            }

            expression = null;
            target = null;
            maxDistance = double.MaxValue;
            return false;
        }

        private static bool TryParseVectorExpression(BsonExpression? expression, Collation collation, out string? fieldExpression, out float[]? target)
        {
            fieldExpression = null;
            target = null;

            if (!IsVectorDistance(expression))
            {
                return false;
            }

            var field = expression.Left;
            if (field == null || string.IsNullOrEmpty(field.Source))
            {
                if (!TryExtractVectorFieldFromSource(expression.Source, out var parsedField))
                {
                    return false;
                }

                fieldExpression = NormalizeVectorField(parsedField);
            }
            else
            {
                fieldExpression = field.Source;
            }

            var targetValue = expression.Right?.ExecuteScalar(collation);
            if (!TryConvertToVector(targetValue, out target))
            {
                return false;
            }

            return true;
        }

        private static bool IsVectorDistance(BsonExpression? expression)
        {
            if (expression == null)
            {
                return false;
            }

            return string.Equals(expression.CustomExpressionName, "VECTOR_DIST", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVectorSimilarity(BsonExpression? expression)
        {
            if (expression == null)
            {
                return false;
            }

            return string.Equals(expression.CustomExpressionName, "VECTOR_SIM", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryConvertToVector(BsonValue? value, out float[]? vector)
        {
            vector = null;

            if (value == null || value.IsNull)
            {
                return false;
            }

#pragma warning disable CS0618
            if (value.Type == BsonType.Vector)
            {
                vector = value.AsVector.ToArray();
                return true;
            }
            #pragma warning restore CS0618

            if (!value.IsArray)
            {
                return false;
            }

            var array = value.AsArray;
            var buffer = new float[array.Count];

            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];

                if (item.IsNull)
                {
                    return false;
                }

                try
                {
                    buffer[i] = (float)item.AsDouble;
                }
                catch
                {
                    return false;
                }
            }

            vector = buffer;
            return true;
        }

        private static bool TryConvertToDouble(BsonValue? value, out double number)
        {
            number = double.NaN;

            if (value == null || value.IsNull || !value.IsNumber)
            {
                return false;
            }

            number = value.AsDouble;
            return !double.IsNaN(number);
        }

        private static bool TryExtractVectorFieldFromSource(string source, out string fieldExpression)
        {
            fieldExpression = null;

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var openIndex = source.IndexOf('(');
            if (openIndex < 0)
            {
                return false;
            }

            var depth = 0;
            for (var i = openIndex + 1; i < source.Length; i++)
            {
                var ch = source[i];

                if (ch == '(')
                {
                    depth++;
                    continue;
                }

                if (ch == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                    continue;
                }

                if (ch == ',' && depth == 0)
                {
                    var segment = source.Substring(openIndex + 1, i - openIndex - 1).Trim();

                    if (string.IsNullOrWhiteSpace(segment))
                    {
                        return false;
                    }

                    fieldExpression = segment;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeVectorField(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return field;
            }

            field = field.Trim();

            if (field.StartsWith("$", StringComparison.Ordinal))
            {
                return field;
            }

            if (field.StartsWith(".", StringComparison.Ordinal))
            {
                field = field.Substring(1);
            }

            return "$." + field;
        }
    }
}
