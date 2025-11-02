namespace LiteDB.Vector
{
    /// <summary>
    /// Represents a vector search result that pairs a document with its computed score.
    /// </summary>
    /// <typeparam name="T">Document type contained in the result.</typeparam>
    /// <param name="Document">The materialized document returned by the query.</param>
    /// <param name="Distance">The computed distance (lower is better).</param>
    /// <param name="Similarity">Optional similarity value when the metric supports it.</param>
    /// <example>
    /// <code><![CDATA[
    /// var matches = collection.Query()
    ///     .TopKNear(x => x.Embedding, queryVector, 5)
    ///     .WithVectorScore()
    ///     .ToList();
    /// VectorMatch<Article> first = matches[0];
    /// ]]></code>
    /// </example>
    public readonly record struct VectorMatch<T>(T Document, double Distance, double? Similarity);

    /// <summary>
    /// Determines which score projection <see cref="LiteQueryableVectorExtensions.WithVectorScore{T}(ILiteQueryableResult{T}, VectorScoreKind)"/> should surface.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var scored = query.WithVectorScore(VectorScoreKind.Similarity).ToList();
    /// ]]></code>
    /// </example>
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
