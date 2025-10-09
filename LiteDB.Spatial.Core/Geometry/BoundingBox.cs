#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents an axis-aligned bounding box in two or three dimensions.
/// </summary>
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    private readonly double[] _values;

    private BoundingBox(double[] values)
    {
        _values = values;
    }

    /// <summary>
    /// Creates a bounding box from raw coordinate values.
    /// </summary>
    /// <param name="values">
    /// The coordinates describing the box. Two-dimensional boxes require four values
    /// (<c>minX</c>, <c>minY</c>, <c>maxX</c>, <c>maxY</c>). Three-dimensional boxes require
    /// six values (<c>minX</c>, <c>minY</c>, <c>minZ</c>, <c>maxX</c>, <c>maxY</c>, <c>maxZ</c>).
    /// </param>
    /// <returns>A new <see cref="BoundingBox"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when the number of values is invalid or any value is non-finite.</exception>
    public static BoundingBox Create(ReadOnlySpan<double> values)
    {
        if (values.Length != 4 && values.Length != 6)
        {
            throw new ArgumentException("Bounding boxes must be described by four (2D) or six (3D) values.", nameof(values));
        }

        var copy = new double[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Bounding box coordinates must be finite numbers.", nameof(values));
            }

            copy[i] = value;
        }

        ValidateExtents(copy);

        return new BoundingBox(copy);
    }

    /// <summary>
    /// Creates a two-dimensional bounding box.
    /// </summary>
    public static BoundingBox From2D(double minX, double minY, double maxX, double maxY)
    {
        return Create(new[] { minX, minY, maxX, maxY });
    }

    /// <summary>
    /// Creates a three-dimensional bounding box.
    /// </summary>
    public static BoundingBox From3D(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        return Create(new[] { minX, minY, minZ, maxX, maxY, maxZ });
    }

    /// <summary>
    /// Gets the number of spatial dimensions represented by the box.
    /// </summary>
    public int Dimensions => _values.Length / 2;

    /// <summary>
    /// Gets a value indicating whether the bounding box captures three dimensions.
    /// </summary>
    public bool Is3D => Dimensions == 3;

    /// <summary>
    /// Gets the minimum X value.
    /// </summary>
    public double MinX => _values[0];

    /// <summary>
    /// Gets the minimum Y value.
    /// </summary>
    public double MinY => _values[1];

    /// <summary>
    /// Gets the minimum Z value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing the Z axis on a 2D box.</exception>
    public double MinZ
    {
        get
        {
            EnsureThreeDimensions();
            return _values[2];
        }
    }

    /// <summary>
    /// Gets the maximum X value.
    /// </summary>
    public double MaxX => _values[Dimensions == 2 ? 2 : 3];

    /// <summary>
    /// Gets the maximum Y value.
    /// </summary>
    public double MaxY => _values[Dimensions == 2 ? 3 : 4];

    /// <summary>
    /// Gets the maximum Z value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing the Z axis on a 2D box.</exception>
    public double MaxZ
    {
        get
        {
            EnsureThreeDimensions();
            return _values[5];
        }
    }

    /// <summary>
    /// Returns the raw coordinate values that describe the box.
    /// </summary>
    public ReadOnlySpan<double> GetValues()
    {
        return _values;
    }

    /// <summary>
    /// Copies the coordinate values into a new array.
    /// </summary>
    public double[] ToArray()
    {
        var copy = new double[_values.Length];
        Array.Copy(_values, copy, _values.Length);
        return copy;
    }

    /// <inheritdoc />
    public bool Equals(BoundingBox other)
    {
        if (Dimensions != other.Dimensions)
        {
            return false;
        }

        for (var i = 0; i < _values.Length; i++)
        {
            if (!_values[i].Equals(other._values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is BoundingBox box && Equals(box);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Dimensions;

            for (var i = 0; i < _values.Length; i++)
            {
                hash = (hash * 397) ^ _values[i].GetHashCode();
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[{string.Join(", ", _values.Select(value => value.ToString(CultureInfo.InvariantCulture)))}]";
    }

    public static bool operator ==(BoundingBox left, BoundingBox right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BoundingBox left, BoundingBox right)
    {
        return !left.Equals(right);
    }

    private static void ValidateExtents(IReadOnlyList<double> values)
    {
        if (values.Count == 4)
        {
            ValidateAxis(values[0], values[2], "X");
            ValidateAxis(values[1], values[3], "Y");
            return;
        }

        ValidateAxis(values[0], values[3], "X");
        ValidateAxis(values[1], values[4], "Y");
        ValidateAxis(values[2], values[5], "Z");
    }

    private static void ValidateAxis(double min, double max, string axis)
    {
        if (max < min)
        {
            throw new ArgumentException($"The maximum {axis} value must be greater than or equal to the minimum value.");
        }
    }

    private void EnsureThreeDimensions()
    {
        if (!Is3D)
        {
            throw new InvalidOperationException("The bounding box does not define a Z axis.");
        }
    }
}
