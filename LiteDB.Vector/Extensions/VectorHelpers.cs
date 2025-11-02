using System;
using LiteDB;

namespace LiteDB.Vector
{
    /// <summary>
    /// Provides utility helpers for constructing and manipulating vector values without referencing <see cref="BsonVector"/> directly.
    /// </summary>
    public static class Vector
    {
        /// <summary>
        /// Creates a <see cref="BsonVector"/> from the supplied values.
        /// </summary>
        /// <param name="values">Vector components to embed in the BSON value.</param>
        /// <returns>A new <see cref="BsonVector"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="values"/> is <c>null</c>.</exception>
        public static BsonVector Create(params float[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return new BsonVector(values);
        }

        /// <summary>
        /// Creates a <see cref="BsonVector"/> from a read-only span of values.
        /// </summary>
        /// <param name="values">Vector components to embed in the BSON value.</param>
        /// <returns>A new <see cref="BsonVector"/> instance.</returns>
        public static BsonVector FromReadOnlySpan(ReadOnlySpan<float> values)
        {
            var buffer = new float[values.Length];
            values.CopyTo(buffer);
            return new BsonVector(buffer);
        }

        /// <summary>
        /// Returns a new vector with unit magnitude using the supplied values.
        /// </summary>
        /// <param name="values">Vector components to normalize.</param>
        /// <returns>A new array containing the normalized vector.</returns>
        /// <exception cref="ArgumentException">Thrown when the magnitude is zero.</exception>
        public static float[] Normalize(ReadOnlySpan<float> values)
        {
            var magnitudeSquared = 0d;

            for (var i = 0; i < values.Length; i++)
            {
                magnitudeSquared += values[i] * values[i];
            }

            if (magnitudeSquared <= double.Epsilon)
            {
                throw new ArgumentException("Vector magnitude must be greater than zero to normalize.", nameof(values));
            }

            var magnitude = Math.Sqrt(magnitudeSquared);
            var normalized = new float[values.Length];

            for (var i = 0; i < values.Length; i++)
            {
                normalized[i] = (float)(values[i] / magnitude);
            }

            return normalized;
        }

        /// <summary>
        /// Computes the dot product between two vectors.
        /// </summary>
        /// <param name="left">First vector.</param>
        /// <param name="right">Second vector.</param>
        /// <returns>The dot product.</returns>
        /// <exception cref="ArgumentException">Thrown when the vectors do not share the same dimensionality.</exception>
        public static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
        {
            if (left.Length != right.Length)
            {
                throw new ArgumentException("Vectors must have the same dimensionality to compute the dot product.", nameof(right));
            }

            var sum = 0d;

            for (var i = 0; i < left.Length; i++)
            {
                sum += left[i] * right[i];
            }

            return sum;
        }

        /// <summary>
        /// Computes the cosine distance between two vectors.
        /// </summary>
        /// <param name="left">First vector.</param>
        /// <param name="right">Second vector.</param>
        /// <returns>The cosine distance in the range [0, 2].</returns>
        /// <exception cref="ArgumentException">Thrown when either vector has zero magnitude.</exception>
        public static double CosineDistance(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
        {
            var leftMagnitudeSquared = 0d;
            var rightMagnitudeSquared = 0d;

            for (var i = 0; i < left.Length; i++)
            {
                leftMagnitudeSquared += left[i] * left[i];
            }

            for (var i = 0; i < right.Length; i++)
            {
                rightMagnitudeSquared += right[i] * right[i];
            }

            if (leftMagnitudeSquared <= double.Epsilon || rightMagnitudeSquared <= double.Epsilon)
            {
                throw new ArgumentException("Cosine distance requires non-zero magnitude vectors.");
            }

            var dot = Dot(left, right);
            var magnitudeProduct = Math.Sqrt(leftMagnitudeSquared) * Math.Sqrt(rightMagnitudeSquared);

            return 1d - (dot / magnitudeProduct);
        }
    }
}
