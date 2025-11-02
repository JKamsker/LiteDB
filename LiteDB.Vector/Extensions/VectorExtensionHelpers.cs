using System;

namespace LiteDB.Vector
{
    internal static class VectorExtensionHelpers
    {
        public static BsonDocument CreateOptionsDocument(VectorIndexOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new BsonDocument
            {
                ["dimensions"] = (int)options.Dimensions,
                ["metric"] = (int)options.Metric
            };
        }
    }
}
