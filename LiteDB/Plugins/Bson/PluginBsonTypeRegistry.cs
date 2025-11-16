using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Bson
{
    /// <summary>
    /// Thread-safe <see cref="IBsonTypeRegistry"/> implementation used by <see cref="DefaultPluginContext"/> to track plugin BSON types per database instance.
    /// </summary>
    public sealed class PluginBsonTypeRegistry : IBsonTypeRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<byte, BsonTypeRegistration> _typesByCode = new Dictionary<byte, BsonTypeRegistration>();
        private readonly Dictionary<string, BsonTypeRegistration> _typesByName = new Dictionary<string, BsonTypeRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<byte, BsonTypeRegistration> _aliases = new Dictionary<byte, BsonTypeRegistration>();

        /// <summary>
        /// Reserved BSON type code ranges for known plugins.
        /// Key: Plugin ID, Value: (MinCode, MaxCode)
        /// </summary>
        private static readonly Dictionary<string, (byte Min, byte Max)> ReservedRanges = new Dictionary<string, (byte, byte)>(StringComparer.Ordinal)
        {
            { "LiteDB.Vector", (0x90, 0x9F) } // 144-159
        };

        /// <inheritdoc />
        public void Register(BsonTypeRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                // Validate type code is within reserved range if plugin ID is known
                if (!string.IsNullOrWhiteSpace(registration.PluginId) &&
                    ReservedRanges.TryGetValue(registration.PluginId, out var range))
                {
                    if (registration.TypeCode < range.Min || registration.TypeCode > range.Max)
                    {
                        throw new InvalidOperationException(
                            $"Plugin '{registration.PluginId}' attempted to register BSON type code 0x{registration.TypeCode:X2} " +
                            $"outside its reserved range 0x{range.Min:X2}-0x{range.Max:X2}.");
                    }
                }

                // Check for conflicts with existing registrations
                if (_typesByCode.TryGetValue(registration.TypeCode, out var existing))
                {
                    throw new InvalidOperationException(
                        $"BSON type code 0x{registration.TypeCode:X2} is already registered by plugin '{existing.PluginId}'. " +
                        $"Plugin '{registration.PluginId}' cannot use the same code.");
                }

                _typesByCode[registration.TypeCode] = registration;

                if (!string.IsNullOrWhiteSpace(registration.Name))
                {
                    _typesByName[registration.Name] = registration;
                }

                if (registration.LegacyAliases != null)
                {
                    foreach (var alias in registration.LegacyAliases)
                    {
                        _aliases[alias] = registration;
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool TryGetByTypeCode(byte typeCode, out BsonTypeRegistration registration)
        {
            lock (_sync)
            {
                if (_typesByCode.TryGetValue(typeCode, out registration))
                {
                    return true;
                }

                return _aliases.TryGetValue(typeCode, out registration);
            }
        }

        /// <inheritdoc />
        public bool TryGetByName(string name, out BsonTypeRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _typesByName.TryGetValue(name, out registration);
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<BsonTypeRegistration> Registered
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
