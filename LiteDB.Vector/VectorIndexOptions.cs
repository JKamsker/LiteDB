using System;

namespace LiteDB.Vector
{
    /// <summary>
    /// Supported metrics for vector similarity operations.
    /// </summary>
    public enum VectorDistanceMetric : byte
    {
        /// <summary>
        /// Euclidean distance (L2 norm).
        /// </summary>
        Euclidean = 0,

        /// <summary>
        /// Cosine similarity.
        /// </summary>
        Cosine = 1,

        /// <summary>
        /// Dot product similarity.
        /// </summary>
        DotProduct = 2
    }

    /// <summary>
    /// Options used when creating a vector-aware index.
    /// </summary>
    public sealed class VectorIndexOptions
    {
        /// <summary>
        /// Gets the expected dimensionality of the indexed vectors.
        /// </summary>
        public ushort Dimensions { get; }

        /// <summary>
        /// Gets the distance metric used when comparing vectors.
        /// </summary>
        public VectorDistanceMetric Metric { get; }

        /// <summary>
        /// Initializes a new instance of VectorIndexOptions.
        /// </summary>
        /// <param name="dimensions">The dimensionality of vectors (must be greater than 0).</param>
        /// <param name="metric">The distance metric to use for similarity comparisons.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when dimensions is 0.</exception>
        public VectorIndexOptions(ushort dimensions, VectorDistanceMetric metric = VectorDistanceMetric.Cosine)
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
