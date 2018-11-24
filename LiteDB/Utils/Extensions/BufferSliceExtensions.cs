﻿using LiteDB.Engine;
using System;
using System.Buffers;
using System.Linq;
using System.Text;
using static LiteDB.Constants;

namespace LiteDB
{
    internal static class BufferSliceExtensions
    {
        #region Read Extensions

        public static Int32 ReadInt32(this BufferSlice buffer, int offset)
        {
            return BitConverter.ToInt32(buffer.Array, buffer.Offset + offset);
        }

        public static UInt32 ReadUInt32(this BufferSlice buffer, int offset)
        {
            return BitConverter.ToUInt32(buffer.Array, buffer.Offset + offset);
        }

        public static Int64 ReadInt64(this BufferSlice buffer, int offset)
        {
            return BitConverter.ToInt64(buffer.Array, buffer.Offset + offset);
        }

        public static double ReadDouble(this BufferSlice buffer, int offset)
        {
            return BitConverter.ToDouble(buffer.Array, buffer.Offset + offset);
        }

        public static Decimal ReadDecimal(this BufferSlice buffer, int offset)
        {
            var a = buffer.ReadInt32(offset);
            var b = buffer.ReadInt32(offset + 4);
            var c = buffer.ReadInt32(offset + 8);
            var d = buffer.ReadInt32(offset + 12);
            return new Decimal(new int[] { a, b, c, d });
        }

        public static ObjectId ReadObjectId(this BufferSlice buffer, int offset)
        {
            return new ObjectId(buffer.Array, buffer.Offset + offset);
        }

        public static Guid ReadGuid(this BufferSlice buffer, int offset)
        {
            return new Guid(buffer.ReadBytes(offset, 16));
        }

        public static byte[] ReadBytes(this BufferSlice buffer, int offset, int count)
        {
            var bytes = new byte[count];

            Buffer.BlockCopy(buffer.Array, buffer.Offset + offset, bytes, 0, count);

            return bytes;
        }

        public static DateTime ReadDateTime(this BufferSlice buffer, int offset)
        {
            return new DateTime(buffer.ReadInt64(offset), DateTimeKind.Utc).ToLocal();
        }

        public static PageAddress ReadPageAddress(this BufferSlice buffer, int offset)
        {
            return new PageAddress(buffer.ReadUInt32(offset), buffer[offset + 4]);
        }

        public static string ReadString(this BufferSlice buffer, int offset, int count)
        {
            return Encoding.UTF8.GetString(buffer.Array, buffer.Offset + offset, count);
        }

        /// <summary>
        /// Read any BsonValue. Use 1 byte for data type, 1 byte for length (optional), 0-255 bytes to value. 
        /// For document or array, use BufferReader
        /// </summary>
        public static BsonValue ReadIndexKey(this BufferSlice buffer, int offset)
        {
            var type = (BsonType)buffer[offset++];

            switch (type)
            {
                case BsonType.Null: return BsonValue.Null;

                case BsonType.Int32: return buffer.ReadInt32(offset);
                case BsonType.Int64: return buffer.ReadInt64(offset);
                case BsonType.Double: return buffer.ReadDouble(offset);
                case BsonType.Decimal: return buffer.ReadDecimal(offset);

                case BsonType.String:
                    var strLength = buffer[offset++];
                    return buffer.ReadString(offset, strLength);

                case BsonType.Document:
                    using (var r = new BufferReader(buffer))
                    {
                        r.Skip(1);
                        return r.ReadDocument();
                    }
                case BsonType.Array:
                    using (var r = new BufferReader(buffer))
                    {
                        r.Skip(1);
                        return r.ReadArray();
                    }

                case BsonType.Binary:
                    var arrLength = buffer[offset++];
                    return buffer.ReadBytes(offset, arrLength);
                case BsonType.ObjectId: return buffer.ReadObjectId(offset);
                case BsonType.Guid: return buffer.ReadGuid(offset);

                case BsonType.Boolean: return buffer[offset] != 0;
                case BsonType.DateTime: return buffer.ReadDateTime(offset);

                case BsonType.MinValue: return BsonValue.MinValue;
                case BsonType.MaxValue: return BsonValue.MaxValue;

                default: throw new NotImplementedException();
            }
        }

        #endregion

        #region Write Extensions

        public static void Write(this BufferSlice buffer, Int32 value, int offset)
        {
            value.ToBytes(buffer.Array, buffer.Offset + offset);
        }

        public static void Write(this BufferSlice buffer, UInt32 value, int offset)
        {
            value.ToBytes(buffer.Array, buffer.Offset + offset);
        }

        public static void Write(this BufferSlice buffer, Int64 value, int offset)
        {
            value.ToBytes(buffer.Array, buffer.Offset + offset);
        }

        public static void Write(this BufferSlice buffer, Double value, int offset)
        {
            value.ToBytes(buffer.Array, buffer.Offset + offset);
        }

        public static void Write(this BufferSlice buffer, Decimal value, int offset)
        {
            var bits = Decimal.GetBits(value);
            buffer.Write(bits[0], offset);
            buffer.Write(bits[1], offset + 4);
            buffer.Write(bits[2], offset + 8);
            buffer.Write(bits[3], offset + 12);
        }

        public static void Write(this BufferSlice buffer, DateTime value, int offset)
        {
            value.ToUtc().Ticks.ToBytes(buffer.Array, buffer.Offset + offset);
        }

        public static void Write(this BufferSlice buffer, PageAddress value, int offset)
        {
            value.PageID.ToBytes(buffer.Array, buffer.Offset + offset);
            buffer.Array[buffer.Offset + offset + 4] = value.Index;
        }

        public static void Write(this BufferSlice buffer, Guid value, int offset)
        {
            buffer.Write(value.ToByteArray(), offset);
        }

        public static void Write(this BufferSlice buffer, ObjectId value, int offset)
        {
            value.ToByteArray(buffer.Array, buffer.Offset + offset);
        }

        public static void Write(this BufferSlice buffer, byte[] value, int offset)
        {
            Buffer.BlockCopy(value, 0, buffer.Array, buffer.Offset + offset, value.Length);
        }

        public static void Write(this BufferSlice buffer, string value, int offset, int count)
        {
            Encoding.UTF8.GetBytes(value, 0, count, buffer.Array, buffer.Offset + offset);
        }

        /// <summary>
        /// Wrtie any BsonValue. Use 1 byte for data type, 1 byte for length (optional), 0-255 bytes to value. 
        /// For document or array, use BufferWriter
        /// </summary>
        public static void WriteIndexKey(this BufferSlice buffer, BsonValue value, int offset)
        {
            buffer[offset++] = (byte)value.Type;

            switch (value.Type)
            {
                case BsonType.Null:
                case BsonType.MinValue:
                case BsonType.MaxValue:
                    break;

                case BsonType.Int32: buffer.Write((Int32)value.RawValue, offset); break;
                case BsonType.Int64: buffer.Write((Int64)value.RawValue, offset); break;
                case BsonType.Double: buffer.Write((Double)value.RawValue, offset); break;
                case BsonType.Decimal: buffer.Write((Decimal)value.RawValue, offset); break;

                case BsonType.String:
                    var str = (string)value.RawValue;
                    var strLength = (byte)Encoding.UTF8.GetByteCount(str);
                    buffer[offset++] = strLength;
                    buffer.Write(str, offset, strLength);
                    break;

                case BsonType.Document:
                    using (var w = new BufferWriter(buffer))
                    {
                        w.Skip(1);
                        w.WriteDocument(value.AsDocument);
                    }
                    break;  
                case BsonType.Array:
                    using (var w = new BufferWriter(buffer))
                    {
                        w.Skip(1);
                        w.WriteArray(value.AsArray);
                    }
                    break;

                case BsonType.Binary:
                    var arr = (Byte[])value.RawValue;
                    buffer[offset++] = (byte)arr.Length;
                    buffer.Write(arr, offset);
                    break;
                case BsonType.ObjectId: buffer.Write((ObjectId)value.RawValue, offset); break;
                case BsonType.Guid: buffer.Write((Guid)value.RawValue, offset); break;

                case BsonType.Boolean: buffer[offset] = ((Boolean)value.RawValue) ? (byte)1 : (byte)0; break;
                case BsonType.DateTime: buffer.Write((DateTime)value.RawValue, offset); break;

                default: throw new NotImplementedException();
            }
        }

        #endregion

    }
}