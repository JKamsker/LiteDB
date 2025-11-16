using System;
using LiteDB;
using LiteDB.Plugins.Indexing;

namespace LiteDB.Vector
{
    internal static class VectorExtensionHelpers
    {
        /// <summary>
        /// Converts <see cref="VectorIndexOptions"/> into the BSON representation expected by the underlying engine.
        /// </summary>
        public static BsonDocument CreateOptionsDocument(VectorIndexOptions options, PluginIndexMetadataDescriptor metadataDescriptor)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var document = new BsonDocument
            {
                ["dimensions"] = (int)options.Dimensions,
                ["metric"] = (int)options.Metric
            };

            if (metadataDescriptor != null)
            {
                var metadata = new BsonDocument
                {
                    ["dimensions"] = (int)options.Dimensions,
                    ["metric"] = (int)options.Metric
                };

                var envelope = new BsonDocument
                {
                    ["pluginId"] = metadataDescriptor.PluginId,
                    ["indexKind"] = metadataDescriptor.IndexKind
                };

                try
                {
                    envelope["payload"] = metadataDescriptor.Serialize(metadata);
                }
                catch (Exception ex)
                {
                    throw new LiteException(0, "Vector metadata serialization failed. See inner exception for details.", ex);
                }

                document["_pluginMetadata"] = envelope;
            }

            return document;
        }
    }
}
