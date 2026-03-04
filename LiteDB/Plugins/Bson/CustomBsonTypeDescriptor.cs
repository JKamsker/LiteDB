using System;
using System.Collections.Generic;

namespace LiteDB.Plugins.Bson
{
    /// <summary>
    /// Registry contract that allows plugins to reserve BSON type codes and provide serialization handlers.
    /// Implementations must be thread-safe because a registry is shared by all collections within a single <see cref="LiteDatabase"/> instance.
    /// </summary>
    public interface ICustomBsonTypeRegistry
    {
        /// <summary>
        /// Registers the supplied plugin BSON type descriptor.
        /// </summary>
        /// <param name="descriptor">Descriptor describing the plugin-owned BSON type.</param>
        void Register(CustomBsonTypeDescriptor descriptor);

        /// <summary>
        /// Attempts to resolve a registration by its reserved type code.
        /// </summary>
        /// <param name="typeCode">The BSON type code.</param>
        /// <param name="descriptor">The registered descriptor if available.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGetByTypeCode(byte typeCode, out CustomBsonTypeDescriptor descriptor);

        /// <summary>
        /// Attempts to resolve a registration by its canonical name.
        /// </summary>
        /// <param name="name">Human-readable name of the BSON type.</param>
        /// <param name="descriptor">The registered descriptor if available.</param>
        /// <returns>True when a descriptor exists.</returns>
        bool TryGetByName(string name, out CustomBsonTypeDescriptor descriptor);

        /// <summary>
        /// Gets the currently registered BSON type descriptors.
        /// </summary>
        IReadOnlyCollection<CustomBsonTypeDescriptor> Registered { get; }
    }

    /// <summary>
    /// Describes a BSON type reserved for plugin-managed serialization.
    /// </summary>
    public sealed class CustomBsonTypeDescriptor
    {
        public CustomBsonTypeDescriptor(
            string pluginId,
            byte typeCode,
            string name,
            Func<BsonValue, int> calculateSize,
            Action<object, BsonValue> serializer,
            Func<object, BsonValue> deserializer,
            Func<BsonValue, string> jsonFormatter,
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

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Type name must be provided.", nameof(name));
            }

            PluginId = pluginId;
            TypeCode = typeCode;
            Name = name;
            CalculateSize = calculateSize ?? throw new ArgumentNullException(nameof(calculateSize));
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            Deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
            JsonFormatter = jsonFormatter ?? throw new ArgumentNullException(nameof(jsonFormatter));
            LegacyAliases = legacyAliases ?? Array.Empty<byte>();
        }

        public string PluginId { get; }

        public byte TypeCode { get; }

        public string Name { get; }

        public Func<BsonValue, int> CalculateSize { get; }

        public Action<object, BsonValue> Serializer { get; }

        public Func<object, BsonValue> Deserializer { get; }

        public Func<BsonValue, string> JsonFormatter { get; }

        public IReadOnlyCollection<byte> LegacyAliases { get; }
    }
}
