using LiteDB;

namespace LiteDB.Vector
{
    /// <summary>
    /// Defines strongly-typed exception helpers for vector-specific error scenarios.
    /// </summary>
    internal static class VectorErrors
    {
        /// <summary>
        /// Creates a <see cref="LiteException"/> indicating that the requested metric does not support similarity projections.
        /// </summary>
        /// <param name="metric">The metric supplied by the caller.</param>
        /// <returns>A configured <see cref="LiteException"/> instance.</returns>
        public static LiteException MetricDoesNotSupportSimilarity(VectorDistanceMetric metric)
        {
            return new LiteException(0, $"Vector metric '{metric}' does not support similarity computations.");
        }
    }
}
