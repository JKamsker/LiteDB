using System;
using System.Globalization;
using System.Linq;

namespace LiteDB.Vector.Document
{
    /// <summary>
    /// Represents a BSON value storing a dense vector. The type is exposed from the LiteDB.Vector package.
    /// </summary>
    public class BsonVector : LiteDB.BsonValue
    {
        public BsonVector(float[] values)
            : base((LiteDB.BsonType)VectorBsonConstants.TypeCode, values ?? throw new ArgumentNullException(nameof(values)))
        {
        }

        /// <summary>
        /// Gets the raw vector components backing this value.
        /// </summary>
        public float[] Values => (float[])this.RawValue;

        /// <summary>
        /// Creates a deep copy of the current vector.
        /// </summary>
        public LiteDB.BsonValue Clone()
        {
            return new BsonVector((float[])Values.Clone());
        }

        internal override int GetBytesCount(bool recalc)
        {
            return sizeof(ushort) + (Values.Length * sizeof(float));
        }

        internal override bool TryWriteJson(LiteDB.JsonWriter writer)
        {
            var array = new LiteDB.BsonArray(Values.Select(x => (LiteDB.BsonValue)x));
            writer.Serialize(array);
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is BsonVector other && Values.SequenceEqual(other.Values);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Values.Aggregate(17, (acc, f) => acc * 31 + f.GetHashCode());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[{string.Join(", ", Values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))}]";
        }
    }
}
