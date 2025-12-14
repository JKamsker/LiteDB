using System;
using System.Buffers.Binary;
using LiteDB.Engine;
using static LiteDB.Constants;

namespace LiteDB.Vector.Engine
{
    /// <summary>
    /// Provides helpers for serializing and mutating the raw byte payload that stores vector index metadata inside collection pages.
    /// </summary>
    internal static class VectorIndexMetadataSerializer
    {
        private const int SlotOffset = 0;
        private const int DimensionsOffset = 1;
        private const int MetricOffset = 3;
        private const int RootOffset = 4;
        private const int ReservedOffset = RootOffset + PageAddress.SIZE;

        /// <summary>
        /// Total number of bytes used to persist vector index metadata.
        /// </summary>
        public const int MetadataLength = 1 + 2 + 1 + PageAddress.SIZE + 4;

        internal static byte[] Read(BufferReader reader)
        {
            var buffer = new byte[MetadataLength];
            reader.Read(buffer, 0, MetadataLength);
            return buffer;
        }

        internal static void Write(BufferWriter writer, byte[] metadata)
        {
            if (metadata.Length != MetadataLength)
            {
                throw new ArgumentException($"Vector metadata payload must contain exactly {MetadataLength} bytes.", nameof(metadata));
            }

            writer.Write(metadata);
        }

        public static byte[] Create(byte slot, ushort dimensions, byte metric)
        {
            var buffer = new byte[MetadataLength];
            SetSlot(buffer, slot);
            SetDimensions(buffer, dimensions);
            SetMetric(buffer, metric);
            SetRoot(buffer, PageAddress.Empty);
            SetReserved(buffer, uint.MaxValue);
            return buffer;
        }

        public static int CalculateSerializedLength(string name)
        {
            return StringEncoding.UTF8.GetByteCount(name) + 1 + MetadataLength;
        }

        public static byte GetSlot(ReadOnlySpan<byte> metadata) => metadata[SlotOffset];

        public static void SetSlot(Span<byte> metadata, byte slot) => metadata[SlotOffset] = slot;

        public static ushort GetDimensions(ReadOnlySpan<byte> metadata)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(metadata.Slice(DimensionsOffset, sizeof(ushort)));
        }

        public static void SetDimensions(Span<byte> metadata, ushort dimensions)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(metadata.Slice(DimensionsOffset, sizeof(ushort)), dimensions);
        }

        public static byte GetMetric(ReadOnlySpan<byte> metadata) => metadata[MetricOffset];

        public static void SetMetric(Span<byte> metadata, byte metric) => metadata[MetricOffset] = metric;

        internal static PageAddress GetRoot(ReadOnlySpan<byte> metadata)
        {
            var pageId = BinaryPrimitives.ReadUInt32LittleEndian(metadata.Slice(RootOffset, sizeof(uint)));
            var index = metadata[RootOffset + sizeof(uint)];
            return new PageAddress(pageId, index);
        }

        internal static void SetRoot(Span<byte> metadata, PageAddress address)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(metadata.Slice(RootOffset, sizeof(uint)), address.PageID);
            metadata[RootOffset + sizeof(uint)] = address.Index;
        }

        public static uint GetReserved(ReadOnlySpan<byte> metadata)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(metadata.Slice(ReservedOffset, sizeof(uint)));
        }

        public static void SetReserved(Span<byte> metadata, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(metadata.Slice(ReservedOffset, sizeof(uint)), value);
        }
    }
}
