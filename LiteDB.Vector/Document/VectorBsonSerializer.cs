using System.Globalization;
using System.Linq;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins.Bson;

namespace LiteDB.Vector.Document
{
    internal static class VectorBsonSerializer
    {
        public static CustomBsonTypeDescriptor CreateDescriptor(string pluginId)
        {
            return new CustomBsonTypeDescriptor(
                pluginId,
                VectorBsonConstants.TypeCode,
                "Vector",
                calculateSize: CalculateSize,
                serializer: Serialize,
                deserializer: Deserialize,
                jsonFormatter: FormatJson,
                legacyAliases: new byte[] { VectorBsonConstants.TypeCode });
        }

        private static int CalculateSize(BsonValue value)
        {
            var vector = GetComponents(value);
            return sizeof(ushort) + (vector.Length * sizeof(float));
        }

        private static void Serialize(object writer, BsonValue value)
        {
            if (writer is BufferWriter bufferWriter)
            {
                var vector = GetComponents(value);
                bufferWriter.Write(vector);
                return;
            }

            throw new LiteException(0, "Vector serialization requires a BufferWriter instance.");
        }

        private static BsonValue Deserialize(object reader)
        {
            if (reader is BufferReader bufferReader)
            {
                var length = bufferReader.ReadUInt16();
                var values = new float[length];

                for (var i = 0; i < length; i++)
                {
                    values[i] = bufferReader.ReadSingle();
                }

                return new BsonVector(values);
            }

            throw new LiteException(0, "Vector deserialization requires a BufferReader instance.");
        }

        private static string FormatJson(BsonValue value)
        {
            var vector = GetComponents(value);
            var formatted = string.Join(",", vector.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
            return $"[{formatted}]";
        }

        internal static float[] GetComponents(BsonValue value)
        {
            return value switch
            {
                BsonVector vector => vector.Values,
                _ when value.RawValue is float[] array => array,
                _ => throw new LiteException(0, "Vector BSON values must store float[] components.")
            };
        }
    }
}
