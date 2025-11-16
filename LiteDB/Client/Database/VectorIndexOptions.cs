using System;

namespace LiteDB
{
    /// <summary>
    /// Options used when creating a vector-aware index.
    /// INTERNAL: This class will be removed in a future version. Vector functionality is moving to LiteDB.Vector plugin.
    /// </summary>
    internal sealed class VectorIndexOptions
    {
        /// <summary>
        /// Gets the expected dimensionality of the indexed vectors.
        /// </summary>
        internal ushort Dimensions { get; }

        /// <summary>
        /// Gets the distance metric used when comparing vectors.
        /// </summary>
        internal VectorDistanceMetric Metric { get; }

        internal VectorIndexOptions(ushort dimensions, VectorDistanceMetric metric = VectorDistanceMetric.Cosine)
        {
            if (dimensions == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Dimensions must be greater than zero.");
            }

            this.Dimensions = dimensions;
            this.Metric = metric;
        }
    }
}
