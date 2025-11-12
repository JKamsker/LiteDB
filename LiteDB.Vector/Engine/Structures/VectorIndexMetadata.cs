using System;
using LiteDB.Engine;
using LiteDB.Plugins.Indexing;

namespace LiteDB.Vector.Engine
{
    /// <summary>
    /// Wraps the raw metadata buffer persisted for a vector-aware index and exposes typed accessors.
    /// </summary>
    internal sealed class VectorIndexMetadata
    {
        private readonly byte[] _buffer;

        private VectorIndexMetadata(byte[] buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

            if (_buffer.Length != VectorIndexMetadataSerializer.MetadataLength)
            {
                throw new ArgumentException($"Vector metadata payload must contain exactly {VectorIndexMetadataSerializer.MetadataLength} bytes.", nameof(buffer));
            }
        }

        public byte Slot => VectorIndexMetadataSerializer.GetSlot(_buffer);

        public ushort Dimensions => VectorIndexMetadataSerializer.GetDimensions(_buffer);

        public byte Metric => VectorIndexMetadataSerializer.GetMetric(_buffer);

        public PageAddress Root
        {
            get => VectorIndexMetadataSerializer.GetRoot(_buffer);
            set => VectorIndexMetadataSerializer.SetRoot(_buffer, value);
        }

        public uint Reserved
        {
            get => VectorIndexMetadataSerializer.GetReserved(_buffer);
            set => VectorIndexMetadataSerializer.SetReserved(_buffer, value);
        }

        internal byte[] Buffer => _buffer;

        public static VectorIndexMetadata Wrap(byte[] buffer) => new VectorIndexMetadata(buffer);

        public static VectorIndexMetadata Create(byte slot, ushort dimensions, byte metric)
        {
            if (dimensions == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Dimensions must be greater than zero");
            }

            var buffer = VectorIndexMetadataSerializer.Create(slot, dimensions, metric);
            return new VectorIndexMetadata(buffer);
        }
    }
}
