using System;
using LiteDB;
using LiteDB.Plugins.Indexing;

namespace LiteDB.Vector.Utils
{
    internal static class VectorMetadataSerializer
    {
        public static byte[] Serialize(BsonDocument metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (!metadata.TryGetValue("slot", out var slotValue) || !slotValue.IsNumber)
            {
                throw new LiteException(0, "Vector metadata serialization requires a numeric 'slot' value.");
            }

            if (!metadata.TryGetValue("dimensions", out var dimensionValue) || !dimensionValue.IsNumber)
            {
                throw new LiteException(0, "Vector metadata serialization requires a numeric 'dimensions' value.");
            }

            if (!metadata.TryGetValue("metric", out var metricValue) || !metricValue.IsNumber)
            {
                throw new LiteException(0, "Vector metadata serialization requires a numeric 'metric' value.");
            }

            return VectorIndexMetadataSerializer.Create(
                (byte)slotValue.AsInt32,
                (ushort)dimensionValue.AsInt32,
                (byte)metricValue.AsInt32);
        }

        public static BsonDocument Deserialize(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return new BsonDocument
            {
                ["slot"] = (int)VectorIndexMetadataSerializer.GetSlot(payload),
                ["dimensions"] = (int)VectorIndexMetadataSerializer.GetDimensions(payload),
                ["metric"] = (int)VectorIndexMetadataSerializer.GetMetric(payload)
            };
        }
    }
}
