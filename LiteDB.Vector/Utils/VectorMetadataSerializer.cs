using System;
using LiteDB;

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

            return BsonSerializer.Serialize(metadata);
        }

        public static BsonDocument Deserialize(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return BsonSerializer.Deserialize(payload);
        }
    }
}
