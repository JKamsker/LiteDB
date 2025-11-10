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

        /// <inheritdoc />
        public void Register(BsonTypeRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
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
