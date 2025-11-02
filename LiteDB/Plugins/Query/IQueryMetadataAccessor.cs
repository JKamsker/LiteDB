using System;
using System.Collections.Generic;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Exposes registration and discovery for plugin-managed query metadata descriptors.
    /// </summary>
    public interface IQueryMetadataAccessor
    {
        /// <summary>
        /// Registers the metadata descriptor for the specified plugin.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <param name="version">Schema version for the metadata bag.</param>
        /// <param name="reservedKeys">Keys that the plugin may store in the metadata bag.</param>
        void Register(string pluginId, int version, IReadOnlyCollection<string> reservedKeys);

        /// <summary>
        /// Attempts to recover the metadata descriptor associated with the supplied plugin identifier.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <param name="descriptor">The descriptor registered for the plugin.</param>
        /// <returns>True when a descriptor has been registered.</returns>
        bool TryGetDescriptor(string pluginId, out QueryMetadataDescriptor descriptor);

        /// <summary>
        /// Gets the metadata descriptor associated with a plugin.
        /// </summary>
        /// <param name="pluginId">Identifier of the plugin that owns the metadata.</param>
        /// <returns>The registered descriptor.</returns>
        QueryMetadataDescriptor GetDescriptor(string pluginId);
    }

    /// <summary>
    /// Describes a plugin-managed query metadata bag registration.
    /// </summary>
    public sealed class QueryMetadataDescriptor
    {
        public QueryMetadataDescriptor(string pluginId, int version, IReadOnlyCollection<string> reservedKeys)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Version must be a positive integer.");
            }

            PluginId = pluginId;
            Version = version;
            ReservedKeys = reservedKeys ?? Array.Empty<string>();
        }

        /// <summary>
        /// Gets the identifier for the plugin that owns the metadata bag.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the schema version declared by the plugin.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Gets the collection of metadata keys reserved by the plugin.
        /// </summary>
        public IReadOnlyCollection<string> ReservedKeys { get; }
    }
}
