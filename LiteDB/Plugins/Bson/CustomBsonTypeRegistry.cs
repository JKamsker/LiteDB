using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Bson
{
    /// <summary>
    /// Thread-safe <see cref="ICustomBsonTypeRegistry"/> implementation used by <see cref="DefaultPluginContext"/> to track plugin BSON types per database instance.
    /// </summary>
    public sealed class CustomBsonTypeRegistry : ICustomBsonTypeRegistry
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<byte, CustomBsonTypeDescriptor> _typesByCode = new Dictionary<byte, CustomBsonTypeDescriptor>();
        private readonly Dictionary<string, CustomBsonTypeDescriptor> _typesByName = new Dictionary<string, CustomBsonTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<byte, CustomBsonTypeDescriptor> _aliases = new Dictionary<byte, CustomBsonTypeDescriptor>();

        public CustomBsonTypeRegistry()
            : this(null)
        {
        }

        internal CustomBsonTypeRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState;
        }

        /// <inheritdoc />
        public void Register(CustomBsonTypeDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            _freezeState?.EnsureNotFrozen();

            ReservedCodeRanges.EnsurePluginOwnsReservedRange(
                descriptor.PluginId,
                descriptor.TypeCode,
                ReservedCodeRanges.VectorBsonStart,
                ReservedCodeRanges.VectorBsonEnd,
                ReservedCodeRanges.VectorPluginId,
                "BSON type code");

            lock (_sync)
            {
                if (_aliases.TryGetValue(descriptor.TypeCode, out var existingByAlias))
                {
                    throw new InvalidOperationException($"BSON type code 0x{descriptor.TypeCode:X2} is already registered as a legacy alias by plugin '{existingByAlias.PluginId}' and cannot be claimed by '{descriptor.PluginId}'.");
                }

                if (_typesByCode.TryGetValue(descriptor.TypeCode, out var existingByCode))
                {
                    ReservedCodeRanges.EnsureUnique(
                        conflictDetected: true,
                        identifierDescription: $"BSON type code 0x{descriptor.TypeCode:X2}",
                        existingPluginId: existingByCode.PluginId,
                        incomingPluginId: descriptor.PluginId);
                }

                if (!string.IsNullOrWhiteSpace(descriptor.Name))
                {
                    if (_typesByName.TryGetValue(descriptor.Name, out var existingByName))
                    {
                        ReservedCodeRanges.EnsureUnique(
                            conflictDetected: true,
                            identifierDescription: $"BSON type '{descriptor.Name}'",
                            existingPluginId: existingByName.PluginId,
                            incomingPluginId: descriptor.PluginId);
                    }

                    _typesByName[descriptor.Name] = descriptor;
                }

                if (descriptor.LegacyAliases != null)
                {
                    foreach (var alias in descriptor.LegacyAliases)
                    {
                        if (alias == descriptor.TypeCode)
                        {
                            continue;
                        }

                        if (alias < 128 && !string.Equals(descriptor.PluginId, "LiteDB.Core", StringComparison.Ordinal))
                        {
                            throw new ArgumentOutOfRangeException(nameof(descriptor.LegacyAliases), "Plugin-reserved BSON type aliases must be >= 128.");
                        }

                        ReservedCodeRanges.EnsurePluginOwnsReservedRange(
                            descriptor.PluginId,
                            alias,
                            ReservedCodeRanges.VectorBsonStart,
                            ReservedCodeRanges.VectorBsonEnd,
                            ReservedCodeRanges.VectorPluginId,
                            "BSON type code");

                        if (_typesByCode.TryGetValue(alias, out var existingByAliasCode))
                        {
                            ReservedCodeRanges.EnsureUnique(
                                conflictDetected: true,
                                identifierDescription: $"BSON type alias 0x{alias:X2}",
                                existingPluginId: existingByAliasCode.PluginId,
                                incomingPluginId: descriptor.PluginId);
                        }

                        if (_aliases.ContainsKey(alias))
                        {
                            ReservedCodeRanges.EnsureUnique(
                                conflictDetected: true,
                                identifierDescription: $"BSON type alias 0x{alias:X2}",
                                existingPluginId: _aliases[alias].PluginId,
                                incomingPluginId: descriptor.PluginId);
                        }

                        _aliases[alias] = descriptor;
                    }
                }

                _typesByCode[descriptor.TypeCode] = descriptor;
            }
        }

        /// <inheritdoc />
        public bool TryGetByTypeCode(byte typeCode, out CustomBsonTypeDescriptor descriptor)
        {
            lock (_sync)
            {
                if (_typesByCode.TryGetValue(typeCode, out descriptor))
                {
                    return true;
                }

                return _aliases.TryGetValue(typeCode, out descriptor);
            }
        }

        /// <inheritdoc />
        public bool TryGetByName(string name, out CustomBsonTypeDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                descriptor = null;
                return false;
            }

            lock (_sync)
            {
                return _typesByName.TryGetValue(name, out descriptor);
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<CustomBsonTypeDescriptor> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _typesByCode.Values.ToArray();
                }
            }
        }
    }
}
