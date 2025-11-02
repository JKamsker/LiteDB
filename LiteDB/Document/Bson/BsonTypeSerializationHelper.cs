using System;
using System.Linq;
using LiteDB.Plugins.Bson;

namespace LiteDB.Document.Bson
{
    internal static class BsonTypeSerializationHelper
    {
        private const string CorePluginId = "LiteDB.Core";

        public static bool TryGetCoreSize(BsonValue value, out int size)
        {
            size = 0;

            if (!BsonTypeResolver.TryGet(null, value.Type, out var registration))
            {
                return false;
            }

            if (!string.Equals(registration.PluginId, CorePluginId, StringComparison.Ordinal))
            {
                throw new LiteException(0, $"BSON type '{registration.TypeCode}' requires plugin '{registration.PluginId}' to serialize.");
            }

            switch (value.Type)
            {
                case BsonType.Vector:
                    if (value.RawValue is float[] vector)
                    {
                        size = 2 + (4 * vector.Length);
                        return true;
                    }
                    throw new LiteException(0, "Vector values must store a float array as raw value.");
                default:
                    return false;
            }
        }

        public static bool TryWriteCoreJson(JsonWriter writer, BsonValue value)
        {
            if (!BsonTypeResolver.TryGet(null, value.Type, out var registration))
            {
                return false;
            }

            if (!string.Equals(registration.PluginId, CorePluginId, StringComparison.Ordinal))
            {
                throw new LiteException(0, $"BSON type '{registration.TypeCode}' requires plugin '{registration.PluginId}' to serialize to JSON.");
            }

            switch (value.Type)
            {
                case BsonType.Vector:
                    if (value.RawValue is float[] vector)
                    {
                        var array = new BsonArray(vector.Select(x => (BsonValue)x));
                        writer.Serialize(array);
                        return true;
                    }
                    throw new LiteException(0, "Vector values must store a float array as raw value.");
                default:
                    return false;
            }
        }
    }
}
