namespace LiteDB.Vector
{
    /// <summary>
    /// Represents a vector search result that pairs a document with its computed score.
    /// </summary>
    /// <param name="Document">The materialized document returned by the query.</param>
    /// <param name="Distance">The computed distance (lower is better).</param>
    /// <param name="Similarity">Optional similarity value when the metric supports it.</param>
    public readonly record struct VectorMatch<T>(T Document, double Distance, double? Similarity);

    /// <summary>
    /// Determines which score projection <see cref="LiteQueryableVectorExtensions.WithVectorScore{T}(ILiteQueryableResult{T}, VectorScoreKind)"/> should surface.
    /// </summary>
    public enum VectorScoreKind
    {
        /// <summary>
        /// Projects the distance produced by the query planner.
        /// </summary>
        Distance = 0,

        /// <summary>
        /// Projects similarity when the metric supports it (e.g., cosine).
        /// </summary>
        Similarity = 1
    }
}
