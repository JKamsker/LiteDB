using System.Linq;

namespace LiteDB.Vector.Document
{
    /// <summary>
    /// Represents a BSON value storing a dense vector. The type is exposed from the LiteDB.Vector package.
    /// </summary>
    public class BsonVector(float[] values) : LiteDB.BsonValue(values)
    {
        /// <summary>
        /// Gets the raw vector components backing this value.
        /// </summary>
        public float[] Values => AsVector;

        /// <summary>
        /// Creates a deep copy of the current vector.
        /// </summary>
        public LiteDB.BsonValue Clone()
        {
            return new BsonVector((float[])Values.Clone());
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
            return $"[{string.Join(", ", Values.Select(v => v.ToString("0.###")))}]";
        }
    }
}
