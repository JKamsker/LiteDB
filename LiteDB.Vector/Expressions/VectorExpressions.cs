using System;
using System.Linq;
using LiteDB;
using LiteDB.Vector.Engine;

namespace LiteDB.Vector
{
    /// <summary>
    /// Provides expression helpers for ad-hoc vector distance and similarity calculations.
    /// </summary>
    internal static class VectorExpressions
    {
        /// <summary>
        /// Computes the metric-specific distance between two vectors. Defaults to cosine distance when no metric is supplied.
        /// </summary>
        /// <param name="left">Expression value representing the first vector.</param>
        /// <param name="right">Expression value representing the second vector.</param>
        /// <returns>The calculated distance, or <see cref="BsonValue.Null"/> when the inputs are invalid.</returns>
        public static BsonValue VectorDistance(BsonValue left, BsonValue right)
        {
            return VectorDistance(left, right, metric: null);
        }

        /// <summary>
        /// Computes the metric-specific distance between two vectors using the provided metric override.
        /// </summary>
        /// <param name="left">Expression value representing the first vector.</param>
        /// <param name="right">Expression value representing the second vector.</param>
        /// <param name="metric">Optional distance metric override. Defaults to cosine when <c>null</c>.</param>
        /// <returns>The calculated distance, or <see cref="BsonValue.Null"/> when the inputs are invalid.</returns>
        public static BsonValue VectorDistance(BsonValue left, BsonValue right, VectorDistanceMetric? metric)
        {
            if (!TryComputeDistance(left, right, metric ?? VectorDistanceMetric.Cosine, out var distance))
            {
                return BsonValue.Null;
            }

            return (double.IsNaN(distance) || double.IsInfinity(distance))
                ? BsonValue.Null
                : distance;
        }

        /// <summary>
        /// Function registration entry point for VECTOR_DIST(left, right).
        /// </summary>
        public static BsonValue VectorDistance(BsonDocument root, Collation collation, BsonDocument parameters, BsonValue left, BsonValue right)
        {
            return VectorDistance(left, right);
        }

        /// <summary>
        /// Function registration entry point for VECTOR_DIST(left, right, metric).
        /// </summary>
        public static BsonValue VectorDistance(BsonDocument root, Collation collation, BsonDocument parameters, BsonValue left, BsonValue right, BsonValue metric)
        {
            if (!TryResolveMetric(metric, out var parsedMetric))
            {
                return BsonValue.Null;
            }

            return VectorDistance(left, right, parsedMetric);
        }

        /// <summary>
        /// Computes cosine similarity between two vectors. Throws when a non-cosine metric is requested.
        /// </summary>
        /// <param name="left">Expression value representing the first vector.</param>
        /// <param name="right">Expression value representing the second vector.</param>
        /// <returns>The cosine similarity in the range [-1, 1], or <see cref="BsonValue.Null"/> when the inputs are invalid.</returns>
        public static BsonValue VectorSimilarity(BsonValue left, BsonValue right)
        {
            return VectorSimilarity(left, right, metric: null);
        }

        /// <summary>
        /// Computes cosine similarity between two vectors using the provided metric override.
        /// </summary>
        /// <param name="left">Expression value representing the first vector.</param>
        /// <param name="right">Expression value representing the second vector.</param>
        /// <param name="metric">Optional metric override. Only cosine is supported; other metrics raise <see cref="LiteException"/>.</param>
        /// <returns>The cosine similarity in the range [-1, 1], or <see cref="BsonValue.Null"/> when the inputs are invalid.</returns>
        public static BsonValue VectorSimilarity(BsonValue left, BsonValue right, VectorDistanceMetric? metric)
        {
            var resolvedMetric = metric ?? VectorDistanceMetric.Cosine;

            if (resolvedMetric != VectorDistanceMetric.Cosine)
            {
                throw VectorErrors.MetricDoesNotSupportSimilarity(resolvedMetric);
            }

            if (!TryComputeDistance(left, right, resolvedMetric, out var distance))
            {
                return BsonValue.Null;
            }

            var similarity = 1d - distance;

            return (double.IsNaN(similarity) || double.IsInfinity(similarity))
                ? BsonValue.Null
                : similarity;
        }

        /// <summary>
        /// Function registration entry point for VECTOR_SIM(left, right).
        /// </summary>
        public static BsonValue VectorSimilarity(BsonDocument root, Collation collation, BsonDocument parameters, BsonValue left, BsonValue right)
        {
            return VectorSimilarity(left, right);
        }

        /// <summary>
        /// Function registration entry point for VECTOR_SIM(left, right, metric).
        /// </summary>
        public static BsonValue VectorSimilarity(BsonDocument root, Collation collation, BsonDocument parameters, BsonValue left, BsonValue right, BsonValue metric)
        {
            if (!TryResolveMetric(metric, out var parsedMetric))
            {
                return BsonValue.Null;
            }

            return VectorSimilarity(left, right, parsedMetric);
        }

        private static bool TryComputeDistance(BsonValue left, BsonValue right, VectorDistanceMetric metric, out double distance)
        {
            distance = double.NaN;

            if (!TryExtractVector(left, out var leftVector) || !TryExtractVector(right, out var rightVector))
            {
                return false;
            }

            if (leftVector.Length == 0 || rightVector.Length == 0 || leftVector.Length != rightVector.Length)
            {
                return false;
            }

            distance = VectorIndexService.ComputeDistance(leftVector, rightVector, metric, out _);
            return !double.IsNaN(distance);
        }

        private static bool TryExtractVector(BsonValue value, out float[] vector)
        {
            vector = Array.Empty<float>();

            if (value.IsNull)
            {
                return false;
            }

            if (value.Type == BsonType.Vector)
            {
                vector = value.AsVector.ToArray();
                return ValidateVector(vector);
            }

            if (value.IsArray)
            {
                var array = value.AsArray;
                var buffer = new float[array.Count];

                for (var i = 0; i < array.Count; i++)
                {
                    if (!TryReadFloat(array[i], out buffer[i]))
                    {
                        vector = Array.Empty<float>();
                        return false;
                    }
                }

                vector = buffer;
                return ValidateVector(vector);
            }

            if (value.IsNumber && TryReadFloat(value, out var scalar))
            {
                vector = new[] { scalar };
                return true;
            }

            return false;
        }

        private static bool TryReadFloat(BsonValue value, out float result)
        {
            result = 0f;

            if (!value.IsNumber)
            {
                return false;
            }

            double number;

            try
            {
                number = value.AsDouble;
            }
            catch
            {
                return false;
            }

            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return false;
            }

            if (number > float.MaxValue || number < float.MinValue)
            {
                return false;
            }

            result = (float)number;
            return !float.IsNaN(result) && !float.IsInfinity(result);
        }

        private static bool ValidateVector(float[] vector)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                var value = vector[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveMetric(BsonValue metric, out VectorDistanceMetric? parsedMetric)
        {
            parsedMetric = null;

            if (metric == null || metric.IsNull)
            {
                return true;
            }

            if (metric.IsNumber)
            {
                var value = metric.AsInt32;
                if (value >= byte.MinValue && value <= byte.MaxValue && Enum.IsDefined(typeof(VectorDistanceMetric), (byte)value))
                {
                    parsedMetric = (VectorDistanceMetric)(byte)value;
                    return true;
                }

                return false;
            }

            if (metric.IsString && Enum.TryParse<VectorDistanceMetric>(metric.AsString, true, out var parsed))
            {
                parsedMetric = parsed;
                return true;
            }

            return false;
        }
    }
}
