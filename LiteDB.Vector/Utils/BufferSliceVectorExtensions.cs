using System;

namespace LiteDB.Vector.Utils
{
    internal static class BufferSliceVectorExtensions
    {
        public static float[] ReadVector(this BufferSlice buffer, int offset)
        {
            var count = buffer.ReadUInt16(offset);
            offset += 2;

            var vector = new float[count];

            for (var i = 0; i < count; i++)
            {
                vector[i] = BitConverter.ToSingle(buffer.Array, buffer.Offset + offset + (i * 4));
            }

            return vector;
        }

        public static void WriteVector(this BufferSlice buffer, float[] value, int offset)
        {
            buffer.Write((ushort)value.Length, offset);
            offset += 2;

            for (var i = 0; i < value.Length; i++)
            {
                BitConverter.GetBytes(value[i]).CopyTo(buffer.Array, buffer.Offset + offset);
                offset += 4;
            }
        }
    }
}
