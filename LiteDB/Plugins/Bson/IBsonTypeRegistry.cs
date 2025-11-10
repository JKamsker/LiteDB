using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiteDB.Plugins.Bson
{
    /// <summary>
    /// Delegate invoked when serializing a value owned by a plugin-managed BSON type.
    /// </summary>
    /// <param name="writer">Serialization target; treated as an opaque dependency until public abstractions are exposed.</param>
    /// <param name="value">Value to serialize.</param>
    public delegate Task PluginBsonSerializer(object writer, object value);

    /// <summary>
    /// Delegate invoked when deserializing data owned by a plugin-managed BSON type.
    /// </summary>
    /// <param name="reader">Source reader; treated as an opaque dependency until public abstractions are exposed.</param>
    /// <returns>The reconstructed value.</returns>
    public delegate Task<object> PluginBsonDeserializer(object reader);

    /// <summary>
    /// Registry contract that allows plugins to reserve BSON type codes and provide serialization handlers.
    /// Implementations must be thread-safe because a registry is shared by all collections within a single <see cref="LiteDatabase"/> instance.
    /// </summary>
    public interface IBsonTypeRegistry
    {
        /// <summary>
        /// Registers the supplied plugin BSON type descriptor.
        /// </summary>
        /// <param name="registration">Descriptor describing the plugin-owned BSON type.</param>
        void Register(BsonTypeRegistration registration);

        /// <summary>
        /// Attempts to resolve a registration by its reserved type code.
        /// </summary>
        /// <param name="typeCode">The BSON type code.</param>
        /// <param name="registration">The registered descriptor if available.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGetByTypeCode(byte typeCode, out BsonTypeRegistration registration);

        /// <summary>
        /// Attempts to resolve a registration by its canonical name.
        /// </summary>
        /// <param name="name">Human-readable name of the BSON type.</param>
        /// <param name="registration">The registered descriptor if available.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGetByName(string name, out BsonTypeRegistration registration);

        /// <summary>
        /// Gets the currently registered BSON type descriptors.
        /// </summary>
        IReadOnlyCollection<BsonTypeRegistration> Registered { get; }
    }

    /// <summary>
    /// Describes a BSON type reserved for plugin-managed serialization.
    /// </summary>
    public sealed class BsonTypeRegistration
    {
        public BsonTypeRegistration(
            string pluginId,
            byte typeCode,
            string name,
            PluginBsonSerializer serializer,
            PluginBsonDeserializer deserializer,
            IReadOnlyCollection<byte> legacyAliases = null)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (typeCode < 128 && !string.Equals(pluginId, "LiteDB.Core", StringComparison.Ordinal))
            {
                throw new ArgumentOutOfRangeException(nameof(typeCode), "Plugin-reserved BSON type codes must be >= 128.");
            }

            PluginId = pluginId;
            TypeCode = typeCode;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            Deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
            LegacyAliases = legacyAliases ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Gets the plugin identifier associated with this registration.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the reserved BSON type code.
        /// </summary>
        public byte TypeCode { get; }

        /// <summary>
        /// Gets the canonical name of the BSON type.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the serializer delegate responsible for encoding plugin values.
        /// </summary>
        public PluginBsonSerializer Serializer { get; }

        /// <summary>
        /// Gets the deserializer delegate responsible for decoding plugin values.
        /// </summary>
        public PluginBsonDeserializer Deserializer { get; }

        /// <summary>
        /// Gets optional legacy aliases that should map to this registration.
        /// </summary>
        public IReadOnlyCollection<byte> LegacyAliases { get; }
    }
}
