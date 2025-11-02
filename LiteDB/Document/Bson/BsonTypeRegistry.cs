using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteDB.Plugins.Bson;

namespace LiteDB.Document.Bson
{
    /// <summary>
    /// Provides plugin-aware BSON type lookup with legacy fallbacks for built-in types.
    /// </summary>
    internal sealed class BsonTypeRegistry
    {
        private readonly IBsonTypeRegistry _pluginRegistry;
        private readonly Dictionary<byte, BsonTypeRegistration> _byCode = new Dictionary<byte, BsonTypeRegistration>();
        private readonly Dictionary<string, BsonTypeRegistration> _byName = new Dictionary<string, BsonTypeRegistration>(StringComparer.Ordinal);

        public BsonTypeRegistry(IBsonTypeRegistry pluginRegistry)
        {
            _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
            RegisterBuiltinTypes();
        }

        /// <summary>
        /// Attempts to locate a type registration for the supplied BSON type code.
        /// </summary>
        public bool TryGet(byte typeCode, out BsonTypeRegistration registration)
        {
            if (_byCode.TryGetValue(typeCode, out registration))
            {
                return true;
            }

            return _pluginRegistry.TryGetByTypeCode(typeCode, out registration);
        }

        /// <summary>
        /// Attempts to locate a type registration by canonical name.
        /// </summary>
        public bool TryGet(string name, out BsonTypeRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                registration = null;
                return false;
            }

            if (_byName.TryGetValue(name, out registration))
            {
                return true;
            }

            return _pluginRegistry.TryGetByName(name, out registration);
        }

        /// <summary>
        /// Registers a legacy core BSON type for fallback handling.
        /// </summary>
        public void RegisterFallback(BsonTypeRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            _byCode[registration.TypeCode] = registration;
            _byName[registration.Name] = registration;

            foreach (var alias in registration.LegacyAliases)
            {
                _byCode[alias] = registration;
            }
        }

        private void RegisterBuiltinTypes()
        {
            RegisterFallback(new BsonTypeRegistration(
                pluginId: "LiteDB.Core",
                typeCode: (byte)BsonType.MinValue,
                name: nameof(BsonType.MinValue),
                serializer: LegacyNotSupportedSerializer,
                deserializer: LegacyNotSupportedDeserializer));

            RegisterFallback(new BsonTypeRegistration(
                pluginId: "LiteDB.Core",
                typeCode: (byte)BsonType.Null,
                name: nameof(BsonType.Null),
                serializer: LegacyNotSupportedSerializer,
                deserializer: LegacyNotSupportedDeserializer));

            RegisterFallback(new BsonTypeRegistration(
                pluginId: "LiteDB.Core",
                typeCode: (byte)BsonType.Vector,
                name: "Vector",
                serializer: LegacyNotSupportedSerializer,
                deserializer: LegacyVectorDeserializer,
                legacyAliases: new ReadOnlyCollection<byte>(new[] { (byte)BsonType.Vector })));
        }

        private static System.Threading.Tasks.Task LegacyNotSupportedSerializer(object writer, object value)
        {
            throw new NotSupportedException("Serialization is delegated to legacy BSON infrastructure.");
        }

        private static System.Threading.Tasks.Task<object> LegacyNotSupportedDeserializer(object reader)
        {
            throw new NotSupportedException("Deserialization is delegated to legacy BSON infrastructure.");
        }

        private static System.Threading.Tasks.Task<object> LegacyVectorDeserializer(object reader)
        {
            throw new NotSupportedException("Vector deserialization requires the LiteDB.Vector plugin.");
        }
    }
}
