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
        private readonly object _sync = new object();
        private readonly Dictionary<byte, CustomBsonTypeDescriptor> _typesByCode = new Dictionary<byte, CustomBsonTypeDescriptor>();
        private readonly Dictionary<string, CustomBsonTypeDescriptor> _typesByName = new Dictionary<string, CustomBsonTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<byte, CustomBsonTypeDescriptor> _aliases = new Dictionary<byte, CustomBsonTypeDescriptor>();

        /// <inheritdoc />
        public void Register(CustomBsonTypeDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            lock (_sync)
            {
                _typesByCode[descriptor.TypeCode] = descriptor;

                if (!string.IsNullOrWhiteSpace(descriptor.Name))
                {
                    _typesByName[descriptor.Name] = descriptor;
                }

                if (descriptor.LegacyAliases != null)
                {
                    foreach (var alias in descriptor.LegacyAliases)
                    {
                        _aliases[alias] = descriptor;
                    }
                }
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
