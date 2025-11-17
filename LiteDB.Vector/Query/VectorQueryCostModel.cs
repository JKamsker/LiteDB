using System;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Query;

namespace LiteDB.Vector.Query
{
    /// <summary>
    /// Provides cost model registration for vector indexes so the planner can consult plugin-provided heuristics.
    /// </summary>
    internal static class VectorQueryCostModel
    {
        public static QueryCostModelRegistration Create(string pluginId)
        {
            if (pluginId == null)
            {
                throw new ArgumentNullException(nameof(pluginId));
            }

            return new QueryCostModelRegistration(
                pluginId,
                ReservedCodeRanges.VectorIndexKind,
                CalculateCost);
        }

        private static double CalculateCost(QueryCostContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var metadata = context.IndexMetadata ?? new BsonDocument();
            var dimensions = TryReadInt(metadata, "dimensions", defaultValue: 1);
            var estimatedDocuments = context.EstimatedDocumentCount > 0
                ? context.EstimatedDocumentCount
                : 1;

            var distanceHint = EstimateDistanceHint(context.Query);
            var baseCost = dimensions * Math.Sqrt(estimatedDocuments);
            var adjusted = baseCost * distanceHint;

            if (double.IsNaN(adjusted) || double.IsInfinity(adjusted) || adjusted <= 0)
            {
                return 1d;
            }

            return adjusted;
        }

        private static int TryReadInt(BsonDocument document, string key, int defaultValue)
        {
            if (document.TryGetValue(key, out var value) && value.IsNumber)
            {
                return Math.Max(1, value.AsInt32);
            }

            return defaultValue;
        }

        private static double EstimateDistanceHint(BsonExpression expression)
        {
            if (expression == null)
            {
                return 1d;
            }

            if ((expression.Type == BsonExpressionType.LessThan || expression.Type == BsonExpressionType.LessThanOrEqual) &&
                expression.Right != null)
            {
                try
                {
                    var threshold = expression.Right.ExecuteScalar(Collation.Binary);

                    if (threshold != null && threshold.IsNumber)
                    {
                        // Treat tighter thresholds as cheaper.
                        var value = threshold.AsDouble;
                        var normalized = value;

                        if (normalized < 0.0001d)
                        {
                            normalized = 0.0001d;
                        }

                        if (normalized > 1d)
                        {
                            normalized = 1d;
                        }

                        return normalized;
                    }
                }
                catch
                {
                    // Ignore scalar evaluation issues and fall back to neutral weighting.
                }
            }

            return 1d;
        }
    }
}
