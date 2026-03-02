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
            byte? orderByMetric = null;
            var orderByConsumable = false;

            if (context.Query.TryGetMetadata(VectorQueryMetadata.PluginId, out var bag))
            {
                metadataBag = bag;
            }
            var metadataIndicatesNormalized = metadataBag != null && VectorQueryMetadata.IsMaxDistanceNormalized(metadataBag);

            if (context.Query.OrderBy.Count > 0 &&
                context.Query.OrderBy[0].Order == LiteDB.Query.Ascending &&
                TryParseVectorExpression(context.Query.OrderBy[0].Expression, collation, out orderByExpression, out orderByTarget, out orderByMetric))
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
                if (TryParseVectorPredicate(term, collation, out expression, out target, out maxDistance, out var predicateMetric))
                {
                    consumedTerm = term;

                    if (predicateMetric.HasValue)
                    {
                        metric = predicateMetric;
                    }

                    // Terms produced by the vector extensions (e.g., WhereNear) store already-normalized distances
                    // and mark that fact via metadata. Manual predicates default to non-normalized values.
                    maxDistanceNormalized = metadataIndicatesNormalized;
                    break;
                }
            }

            if (expression == null && orderByConsumable)
            {
                expression = orderByExpression;
                target = orderByTarget;
                maxDistance = double.MaxValue;
            }

            if (!metric.HasValue && orderByConsumable && orderByMetric.HasValue)
            {
                metric = orderByMetric;
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
                            if (!metric.HasValue)
                            {
                                metric = bagMetric;
                            }
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

            var defaultMetric = (byte)VectorDistanceMetric.Cosine;
            var effectiveMetric = metric ?? defaultMetric;
            var effectiveOrderByMetric = orderByMetric ?? defaultMetric;

            var orderByMatchesVectorQuery = orderByConsumable &&
                string.Equals(orderByExpression, expression, StringComparison.OrdinalIgnoreCase) &&
                TargetsEqual(orderByTarget, target) &&
                effectiveOrderByMetric == effectiveMetric;

            int? limit = context.Query.Limit != int.MaxValue ? context.Query.Limit : (int?)null;
            var offset = context.Query.Offset;

            if (!limit.HasValue && offset > 0)
            {
                // OFFSET without LIMIT requires retrieving (potentially) the full result set.
                // Vector searches use a finite candidate cap, which can underfill OFFSET-based paging.
                return false;
            }

            if (limit.HasValue &&
                context.Query.OrderBy.Count > 0 &&
                !orderByMatchesVectorQuery)
            {
                // LIMIT + non-vector ordering requires full semantics. Avoid rewriting to the vector index
                // because approximate candidate truncation (DefaultEfSearch) can silently underfill LIMIT.
                return false;
            }

            if (!limit.HasValue &&
                context.Query.OrderBy.Count > 0 &&
                orderByMatchesVectorQuery &&
                consumedTerm == null)
            {
                // ORDER BY without LIMIT requires retrieving (potentially) the full ordered result set.
                // Vector searches use a finite candidate cap, which can underfill ORDER BY semantics.
                return false;
            }

            if (limit.HasValue && offset > 0)
            {
                var required = (long)offset + limit.Value;

                if (required > int.MaxValue)
                {
                    return false;
                }

                limit = (int)required;
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

                var resolvedMetric = metric ?? (metadataBag != null ? metadata.Metric : defaultMetric);

                if (resolvedMetric != metadata.Metric)
                {
                    continue;
                }

                var effectiveMaxDistance = VectorEnsure.NormalizeMaxDistance(maxDistance, resolvedMetric, maxDistanceNormalized);

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

                var resolvedOrderByMetric = orderByMetric ?? resolvedMetric;

                context.OrderByConsumed = orderByConsumable &&
                    string.Equals(orderByExpression, expression, StringComparison.OrdinalIgnoreCase) &&
                    TargetsEqual(orderByTarget, target) &&
                    resolvedOrderByMetric == resolvedMetric;
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
            return TryParseVectorPredicate(predicate, collation, out expression, out target, out maxDistance, out _);
        }

        private static bool TryParseVectorPredicate(BsonExpression? predicate, Collation collation, out string? expression, out float[]? target, out double maxDistance, out byte? metric)
        {
            expression = null;
            target = null;
            maxDistance = double.MaxValue;
            metric = null;

            if (predicate == null)
            {
                return false;
            }

            if ((predicate.Type == BsonExpressionType.LessThan || predicate.Type == BsonExpressionType.LessThanOrEqual) &&
                TryParseVectorExpression(predicate.Left, collation, out expression, out target, out metric) &&
                TryConvertToDouble(predicate.Right?.ExecuteScalar(collation), out maxDistance))
            {
                return true;
            }

            if ((predicate.Type == BsonExpressionType.GreaterThan || predicate.Type == BsonExpressionType.GreaterThanOrEqual) &&
                TryParseVectorExpression(predicate.Right, collation, out expression, out target, out metric) &&
                TryConvertToDouble(predicate.Left?.ExecuteScalar(collation), out maxDistance))
            {
                return true;
            }

            expression = null;
            target = null;
            maxDistance = double.MaxValue;
            metric = null;
            return false;
        }

        private static bool TryParseVectorExpression(BsonExpression? expression, Collation collation, out string? fieldExpression, out float[]? target)
        {
            return TryParseVectorExpression(expression, collation, out fieldExpression, out target, out _);
        }

        private static bool TryParseVectorExpression(BsonExpression? expression, Collation collation, out string? fieldExpression, out float[]? target, out byte? metric)
        {
            fieldExpression = null;
            target = null;
            metric = null;

            if (!IsVectorDistance(expression))
            {
                return false;
            }

            var field = expression.Left;
            string? targetSource = null;
            string? metricSource = null;

            if (field == null || string.IsNullOrEmpty(field.Source))
            {
                if (!TryExtractVectorArgumentsFromSource(expression.Source, out var parsedField, out targetSource, out metricSource))
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
                    if (metricSource == null || TryResolveMetricArgument(expression, metricSource, out metric))
                    {
                        return true;
                    }

                    return false;
                }

                return false;
            }

            var targetValue = expression.Right?.ExecuteScalar(collation);
            if (TryConvertToVector(targetValue, out target))
            {
                if (metricSource == null || TryResolveMetricArgument(expression, metricSource, out metric))
                {
                    return true;
                }

                return false;
            }

            if (TryExtractVectorArgumentsFromSource(expression.Source, out _, out targetSource, out metricSource) &&
                TryResolveVectorArgument(expression, targetSource, out target))
            {
                if (metricSource == null || TryResolveMetricArgument(expression, metricSource, out metric))
                {
                    return true;
                }

                return false;
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
                var candidate = bsonVector.Values.ToArray();

                for (var i = 0; i < candidate.Length; i++)
                {
                    var item = candidate[i];

                    if (float.IsNaN(item) || float.IsInfinity(item))
                    {
                        return false;
                    }
                }

                vector = candidate;
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
                    var floatValue = (float)item.AsDouble;

                    if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    {
                        return false;
                    }

                    buffer[i] = floatValue;
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

        private static bool TryExtractVectorArgumentsFromSource(string source, out string fieldExpression, out string targetExpression, out string? metricExpression)
        {
            fieldExpression = null;
            targetExpression = null;
            metricExpression = null;

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

            if (segments.Count >= 3)
            {
                metricExpression = segments[2];
            }

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

        private static bool TryResolveMetricArgument(BsonExpression expression, string source, out byte? metric)
        {
            metric = null;

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
                    TryConvertToMetric(parameterValue, out metric))
                {
                    return true;
                }

                return false;
            }

            if ((source.StartsWith("'", StringComparison.Ordinal) && source.EndsWith("'", StringComparison.Ordinal)) ||
                (source.StartsWith("\"", StringComparison.Ordinal) && source.EndsWith("\"", StringComparison.Ordinal)))
            {
                source = source.Substring(1, source.Length - 2);
            }

            var trimmed = source.Trim();
            var lastSegment = trimmed.Split('.').LastOrDefault() ?? trimmed;

            if (byte.TryParse(lastSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) &&
                Enum.IsDefined(typeof(VectorDistanceMetric), numeric))
            {
                metric = numeric;
                return true;
            }

            if (lastSegment.Length > 0 &&
                (char.IsDigit(lastSegment[0]) || lastSegment[0] == '+' || lastSegment[0] == '-'))
            {
                return false;
            }

            if (Enum.TryParse<VectorDistanceMetric>(lastSegment, true, out var parsed) &&
                Enum.IsDefined(typeof(VectorDistanceMetric), parsed))
            {
                metric = (byte)parsed;
                return true;
            }

            return false;
        }

        private static bool TryConvertToMetric(BsonValue? value, out byte? metric)
        {
            metric = null;

            if (value == null || value.IsNull)
            {
                return false;
            }

            if (value.IsNumber)
            {
                int numeric;

                try
                {
                    numeric = value.AsInt32;
                }
                catch
                {
                    return false;
                }

                if (numeric < byte.MinValue || numeric > byte.MaxValue)
                {
                    return false;
                }

                var numericByte = (byte)numeric;

                if (Enum.IsDefined(typeof(VectorDistanceMetric), numericByte))
                {
                    metric = numericByte;
                    return true;
                }

                return false;
            }

            if (value.IsString &&
                Enum.TryParse<VectorDistanceMetric>(value.AsString, true, out var parsed) &&
                Enum.IsDefined(typeof(VectorDistanceMetric), parsed))
            {
                metric = (byte)parsed;
                return true;
            }

            return false;
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

                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
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
