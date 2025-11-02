namespace LiteDB.Vector
{
    /// <summary>
    /// Supported metrics for vector similarity operations.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var options = new VectorIndexOptions(384, VectorDistanceMetric.Cosine);
    /// ]]></code>
    /// </example>
    public enum VectorDistanceMetric : byte
    {
        /// <summary>
        /// Uses Euclidean distance to rank vectors.
        /// </summary>
        Euclidean = 0,

        /// <summary>
        /// Uses Cosine distance (default) to rank vectors.
        /// </summary>
        Cosine = 1,

        /// <summary>
        /// Uses Dot Product similarity to rank vectors.
        /// </summary>
        DotProduct = 2
    }
}
