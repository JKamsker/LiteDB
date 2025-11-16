using System;

namespace LiteDB.Plugins.Indexing
{
    /// <summary>
    /// Registry for plugin-defined index metadata serializers.
    /// Collection pages persist metadata as opaque blobs; plugins provide serialization logic.
    /// </summary>
    public interface IPluginIndexMetadataRegistry
    {
        /// <summary>
        /// Registers a metadata serializer descriptor for a specific index kind.
        /// </summary>
        /// <param name="descriptor">The descriptor to register.</param>
        /// <exception cref="ArgumentNullException">When descriptor is null.</exception>
        /// <exception cref="InvalidOperationException">When an index kind is already registered.</exception>
        void Register(PluginIndexMetadataDescriptor descriptor);

        /// <summary>
        /// Attempts to retrieve a metadata descriptor by index kind.
        /// </summary>
        /// <param name="indexKind">The index kind identifier (e.g., "vector.hnsw").</param>
        /// <param name="descriptor">Receives the descriptor if found.</param>
        /// <returns>True if the descriptor was found, false otherwise.</returns>
        bool TryGet(string indexKind, out PluginIndexMetadataDescriptor descriptor);

        /// <summary>
        /// Gets a metadata descriptor by index kind.
        /// </summary>
        /// <param name="indexKind">The index kind identifier.</param>
        /// <returns>The metadata descriptor.</returns>
        /// <exception cref="KeyNotFoundException">When no descriptor is registered for the index kind.</exception>
        PluginIndexMetadataDescriptor Get(string indexKind);
    }

    /// <summary>
    /// Describes how to serialize and deserialize metadata for a plugin-owned index type.
    /// </summary>
    public sealed class PluginIndexMetadataDescriptor
    {
        /// <summary>
        /// Initializes a new metadata descriptor.
        /// </summary>
        /// <param name="pluginId">The identifier of the owning plugin (e.g., "LiteDB.Vector").</param>
        /// <param name="indexKind">The logical index kind (e.g., "vector.hnsw").</param>
        /// <param name="serialize">Delegate that converts metadata to bytes.</param>
        /// <param name="deserialize">Delegate that converts bytes to metadata.</param>
        public PluginIndexMetadataDescriptor(
            string pluginId,
            string indexKind,
            Func<BsonDocument, byte[]> serialize,
            Func<byte[], BsonDocument> deserialize)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("Plugin ID cannot be null or whitespace.", nameof(pluginId));

            if (string.IsNullOrWhiteSpace(indexKind))
                throw new ArgumentException("Index kind cannot be null or whitespace.", nameof(indexKind));

            PluginId = pluginId;
            IndexKind = indexKind;
            Serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            Deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
        }

        /// <summary>
        /// Gets the identifier of the plugin that owns this index type.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical index kind (e.g., "vector.hnsw", "vector.ivfflat").
        /// </summary>
        public string IndexKind { get; }

        /// <summary>
        /// Gets the delegate that serializes metadata from options to bytes.
        /// </summary>
        public Func<BsonDocument, byte[]> Serialize { get; }

        /// <summary>
        /// Gets the delegate that deserializes bytes back to metadata.
        /// </summary>
        public Func<byte[], BsonDocument> Deserialize { get; }
    }

    /// <summary>
    /// Generic metadata payload stored in collection pages for plugin-owned indexes.
    /// Core persists this as: {pluginIdLength:byte}{pluginId:utf8}{payloadLength:ushort}{payload:bytes}
    /// </summary>
    public sealed class PluginIndexMetadata
    {
        /// <summary>
        /// Initializes a new plugin index metadata instance.
        /// </summary>
        public PluginIndexMetadata(string pluginId, string indexKind, byte[] payload)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("Plugin ID cannot be null or whitespace.", nameof(pluginId));

            if (string.IsNullOrWhiteSpace(indexKind))
                throw new ArgumentException("Index kind cannot be null or whitespace.", nameof(indexKind));

            PluginId = pluginId;
            IndexKind = indexKind;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>
        /// Gets the identifier of the plugin that owns this metadata.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the logical index kind.
        /// </summary>
        public string IndexKind { get; }

        /// <summary>
        /// Gets the opaque payload bytes managed by the plugin.
        /// </summary>
        public byte[] Payload { get; }
    }
}
