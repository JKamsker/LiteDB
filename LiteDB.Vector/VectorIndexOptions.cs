using System;

namespace LiteDB.Vector
{
    /// <summary>
    /// Options used when creating a vector-aware index.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var options = new VectorIndexOptions(dimensions: 384, metric: VectorDistanceMetric.Cosine);
    /// collection.EnsureIndex(x => x.Embedding, options);
    /// ]]></code>
    /// </example>
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
        /// Initializes a new instance of the <see cref="VectorIndexOptions"/> class.
        /// </summary>
        /// <param name="dimensions">Expected dimensionality of the vector field.</param>
        /// <param name="metric">Distance metric used for comparisons.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dimensions"/> is zero.</exception>
        /// <example>
        /// <code><![CDATA[
        /// var options = new VectorIndexOptions(768, VectorDistanceMetric.Euclidean);
        /// ]]></code>
        /// </example>
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
