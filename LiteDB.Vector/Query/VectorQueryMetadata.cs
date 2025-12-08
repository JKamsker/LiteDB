using LiteDB.Plugins.Query;

namespace LiteDB.Vector.Query
{
    /// <summary>
    /// Provides shared constants describing query metadata allocations used by the vector plugin.
    /// </summary>
    internal static class VectorQueryMetadata
    {
        internal const string PluginId = "LiteDB.Vector";
        internal const int Version = 3;

        internal const string FieldKey = "VectorField";
        internal const string TargetKey = "TargetEmbedding";
        internal const string MaxDistanceKey = "VectorMaxDistance";
        internal const string MaxDistanceNormalizedKey = "VectorMaxDistanceNormalized";
        internal const string MetricKey = "VectorMetric";

        internal static readonly string[] ReservedKeys = new[]
        {
            FieldKey,
            TargetKey,
            MaxDistanceKey,
            MaxDistanceNormalizedKey,
            MetricKey
        };

        internal static bool IsMaxDistanceNormalized(QueryMetadataBag metadata)
        {
            if (metadata == null)
            {
                return false;
            }

            if (metadata.Version >= Version)
            {
                return metadata.TryGet<bool>(MaxDistanceNormalizedKey, out var normalized) && normalized;
            }

            return metadata.Version >= 2;
        }
    }
}
