using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Vector.Document;
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
            QueryMetadataBag? metadataBag = null;
            var maxDistanceNormalized = false;
            string? orderByExpression = null;
            float[]? orderByTarget = null;
            var orderByConsumable = false;

            if (context.Query.TryGetMetadata(VectorQueryMetadata.PluginId, out var bag))
            {
                metadataBag = bag;
            }
            var metadataIndicatesNormalized = metadataBag != null && VectorQueryMetadata.IsMaxDistanceNormalized(metadataBag);

            if (context.Query.OrderBy.Count > 0 &&
                context.Query.OrderBy[0].Order == LiteDB.Query.Ascending &&
                TryParseVectorExpression(context.Query.OrderBy[0].Expression, collation, out orderByExpression, out orderByTarget))
            {
                if (context.Query.OrderBy.Count == 1)
                {
                    orderByConsumable = true;
                }
                else if (context.Query.OrderBy.Count == 2 &&
                    context.Query.OrderBy[1].Order == LiteDB.Query.Ascending &&
                    IsIdOrderByExpression(context.Query.OrderBy[1].Expression))
                {
                    // Allow deterministic tie-breaking by _id without forcing a secondary in-engine sort.
                    orderByConsumable = true;
                }
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

            if (expression == null && orderByConsumable)
            {
                expression = orderByExpression;
                target = orderByTarget;
                maxDistance = double.MaxValue;
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
                            maxDistanceNormalized = metadataIndicatesNormalized;
                        }

                        if (metadataBag.TryGet<byte?>(VectorQueryMetadata.MetricKey, out var bagMetric))
                        {
                            metric = bagMetric;
                        }

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
                            maxDistanceNormalized = metadataIndicatesNormalized;
                        }

                        if (!metric.HasValue &&
                            metadataBag.TryGet<byte?>(VectorQueryMetadata.MetricKey, out var bagMetric))
                        {
                            metric = bagMetric;
                        }
                    }
                }
            }


            if (expression == null || target == null)
            {
                return false;
            }

            var orderByMatchesVectorQuery = orderByConsumable &&
                string.Equals(orderByExpression, expression, StringComparison.OrdinalIgnoreCase) &&
                TargetsEqual(orderByTarget, target);

            int? limit = context.Query.Limit != int.MaxValue ? context.Query.Limit : (int?)null;
            var offset = context.Query.Offset;

            if (limit.HasValue &&
                context.Query.OrderBy.Count > 0 &&
                !orderByMatchesVectorQuery)
            {
                // Avoid pushing LIMIT when ORDER BY requires post-processing, otherwise LIMIT/OFFSET semantics
                // can be corrupted by early truncation (especially with additional ORDER BY segments).
                limit = null;
            }
            else if (limit.HasValue && offset > 0)
            {
                var required = (long)offset + limit.Value;
                limit = required > int.MaxValue ? int.MaxValue : (int)required;
            }

            foreach (var (index, pluginId, metadataBuffer) in collection.GetPluginIndexes())
            {
                if (!string.Equals(pluginId, VectorPlugin.PluginId, StringComparison.Ordinal))
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
                var metadataDocument = VectorMetadataSerializer.Deserialize(metadataBuffer);

                context.UseIndex(
                    vectorIndex,
                    index.Expression,
                    consumed,
                    isIndexKeyOnly: false,
                    indexCost: vectorIndex.GetCost(index),
                    pluginId: VectorPlugin.PluginId,
                    pluginIndexKind: VectorPlugin.IndexKind,
                    pluginMetadata: metadataDocument);

                context.OrderByConsumed = orderByConsumable &&
                    string.Equals(orderByExpression, expression, StringComparison.OrdinalIgnoreCase) &&
                    TargetsEqual(orderByTarget, target);
                return true;
            }

            return false;
        }

        private static bool TargetsEqual(float[]? left, float[]? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdOrderByExpression(BsonExpression? expression)
        {
            var source = expression?.Source?.Trim();

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return string.Equals(source, "$._id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source, "_id", StringComparison.OrdinalIgnoreCase);
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
            string? targetSource = null;

            if (field == null || string.IsNullOrEmpty(field.Source))
            {
                if (!TryExtractVectorArgumentsFromSource(expression.Source, out var parsedField, out targetSource))
                {
                    return false;
                }

                fieldExpression = NormalizeVectorField(parsedField);
            }
            else
            {
                fieldExpression = field.Source;
            }

            if (targetSource != null)
            {
                if (TryResolveVectorArgument(expression, targetSource, out target))
                {
                    return true;
                }

                return false;
            }

            var targetValue = expression.Right?.ExecuteScalar(collation);
            if (TryConvertToVector(targetValue, out target))
            {
                return true;
            }

            if (TryExtractVectorArgumentsFromSource(expression.Source, out _, out targetSource) &&
                TryResolveVectorArgument(expression, targetSource, out target))
            {
                return true;
            }

            return false;
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
            if (value is BsonVector bsonVector)
            {
                vector = bsonVector.Values.ToArray();
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

        private static bool TryExtractVectorArgumentsFromSource(string source, out string fieldExpression, out string targetExpression)
        {
            fieldExpression = null;
            targetExpression = null;

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var openIndex = source.IndexOf('(');
            if (openIndex < 0)
            {
                return false;
            }

            var segmentStart = openIndex + 1;
            var parenDepth = 0;
            var bracketDepth = 0;
            var inString = false;
            var stringDelimiter = '\0';
            var escape = false;
            var segments = new List<string>(capacity: 3);

            for (var i = segmentStart; i < source.Length; i++)
            {
                var ch = source[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (ch == stringDelimiter)
                    {
                        inString = false;
                        stringDelimiter = '\0';
                    }

                    continue;
                }

                if (ch == '\'' || ch == '"')
                {
                    inString = true;
                    stringDelimiter = ch;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                    continue;
                }

                if (ch == ']')
                {
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    continue;
                }

                if (ch == '(')
                {
                    parenDepth++;
                    continue;
                }

                if (ch == ')')
                {
                    if (parenDepth == 0)
                    {
                        var segment = source.Substring(segmentStart, i - segmentStart).Trim();
                        if (!string.IsNullOrWhiteSpace(segment))
                        {
                            segments.Add(segment);
                        }

                        break;
                    }

                    parenDepth--;
                    continue;
                }

                if (ch == ',' && parenDepth == 0 && bracketDepth == 0)
                {
                    var segment = source.Substring(segmentStart, i - segmentStart).Trim();

                    if (string.IsNullOrWhiteSpace(segment))
                    {
                        return false;
                    }

                    segments.Add(segment);
                    segmentStart = i + 1;
                }
            }

            if (segments.Count < 2)
            {
                return false;
            }

            fieldExpression = segments[0];
            targetExpression = segments[1];

            return !string.IsNullOrWhiteSpace(fieldExpression) &&
                !string.IsNullOrWhiteSpace(targetExpression);
        }

        private static bool TryResolveVectorArgument(BsonExpression expression, string source, out float[]? vector)
        {
            vector = null;

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            source = source.Trim();

            if (source.StartsWith("@", StringComparison.Ordinal))
            {
                var key = source.Substring(1);

                if (expression.Parameters != null &&
                    expression.Parameters.TryGetValue(key, out var parameterValue) &&
                    TryConvertToVector(parameterValue, out vector))
                {
                    return true;
                }

                return false;
            }

            return TryParseVectorLiteral(source, out vector);
        }

        private static bool TryParseVectorLiteral(string source, out float[]? vector)
        {
            vector = null;

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            source = source.Trim();

            if (!source.StartsWith("[", StringComparison.Ordinal) || !source.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            var inner = source.Substring(1, source.Length - 2);
            if (string.IsNullOrWhiteSpace(inner))
            {
                return false;
            }

            var parts = inner.Split(',');
            var buffer = new float[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();

                if (part.Length == 0 ||
                    !float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return false;
                }

                buffer[i] = value;
            }

            vector = buffer;
            return true;
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
