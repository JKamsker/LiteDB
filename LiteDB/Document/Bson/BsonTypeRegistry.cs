using System;
using LiteDB.Plugins.Bson;

namespace LiteDB.Document.Bson
{
    /// <summary>
    /// Provides plugin-aware BSON type lookup.
    /// </summary>
    internal sealed class BsonTypeRegistry
    {
        private readonly ICustomBsonTypeRegistry _pluginRegistry;

        public BsonTypeRegistry(ICustomBsonTypeRegistry pluginRegistry)
        {
            _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        }

        public bool TryGet(byte typeCode, out CustomBsonTypeDescriptor registration)
        {
            return _pluginRegistry.TryGetByTypeCode(typeCode, out registration);
        }

        public bool TryGet(string name, out CustomBsonTypeDescriptor registration)
        {
            return _pluginRegistry.TryGetByName(name, out registration);
        }
    }
}


