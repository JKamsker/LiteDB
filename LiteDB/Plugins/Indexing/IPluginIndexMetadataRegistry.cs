using System;
using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Plugins.Indexing
{
    /// <summary>
    /// Registry for plugin-defined index metadata serializers.
    /// Collection pages store opaque metadata blobs; plugins provide serialization logic.
    /// </summary>
    public interface IPluginIndexMetadataRegistry
    {
        /// <summary>
        /// Registers a metadata descriptor.
        /// </summary>
        /// <param name="descriptor">Descriptor describing the serializer implementation.</param>
        void Register(PluginIndexMetadataDescriptor descriptor);

        /// <summary>
        /// Attempts to resolve a descriptor by index kind.
        /// </summary>
        /// <param name="indexKind">Logical index kind identifier.</param>
        /// <param name="descriptor">Receives the descriptor when found.</param>
        /// <returns>True when a descriptor was registered.</returns>
        bool TryGet(string indexKind, out PluginIndexMetadataDescriptor descriptor);

        /// <summary>
        /// Resolves a descriptor by index kind.
        /// </summary>
        /// <param name="indexKind">Logical index kind identifier.</param>
        /// <returns>The registered descriptor.</returns>
        PluginIndexMetadataDescriptor Get(string indexKind);

        /// <summary>
        /// Gets all registered metadata descriptors.
        /// </summary>
        IReadOnlyCollection<PluginIndexMetadataDescriptor> Registered { get; }
    }

    /// <summary>
    /// Describes serialization logic for a plugin-owned index metadata payload.
    /// </summary>
    public sealed class PluginIndexMetadataDescriptor
    {
        public PluginIndexMetadataDescriptor(
            string pluginId,
            string indexKind,
            Func<BsonDocument, byte[]> serialize,
            Func<byte[], BsonDocument> deserialize)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(indexKind))
            {
                throw new ArgumentException("Index kind must be provided.", nameof(indexKind));
            }

            PluginId = pluginId;
            IndexKind = indexKind;
            Serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            Deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
        }

        /// <summary>
        /// Gets the plugin identifier that owns the serializer.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical index kind associated with the serializer.
        /// </summary>
        public string IndexKind { get; }

        /// <summary>
        /// Gets the delegate used to serialize metadata documents into payloads.
        /// </summary>
        public Func<BsonDocument, byte[]> Serialize { get; }

        /// <summary>
        /// Gets the delegate used to deserialize payloads back into metadata documents.
        /// </summary>
        public Func<byte[], BsonDocument> Deserialize { get; }
    }

    /// <summary>
    /// Represents the metadata payload persisted for plugin-owned indexes.
    /// </summary>
    public sealed class PluginIndexMetadata
    {
        public PluginIndexMetadata(string pluginId, string indexKind, byte[] payload)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(indexKind))
            {
                throw new ArgumentException("Index kind must be provided.", nameof(indexKind));
            }

            PluginId = pluginId;
            IndexKind = indexKind;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>
        /// Gets the plugin identifier associated with the metadata payload.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical index kind associated with the payload.
        /// </summary>
        public string IndexKind { get; }

        /// <summary>
        /// Gets the serialized payload bytes.
        /// </summary>
        public byte[] Payload { get; }
    }
}
