namespace LiteDB.Vector
{
    /// <summary>
    /// Shared identifiers and reserved codes used by the LiteDB.Vector plugin.
    /// </summary>
    public static class VectorPlugin
    {
        /// <summary>
        /// Gets the plugin identifier used when registering metadata and diagnostics.
        /// </summary>
        public const string PluginId = "LiteDB.Vector";

        internal const string StrategyKind = "vector";
        internal const string IndexKind = "vector.hnsw";
        internal const byte BsonTypeCode = 0x90;
        internal const byte PageTypeCode = 0xE0;
    }
}
