#nullable enable

using System;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a three-dimensional Cartesian coordinate.
/// </summary>
public readonly struct GeoPoint3D : IEquatable<GeoPoint3D>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPoint3D"/> struct.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    /// <exception cref="ArgumentException">Thrown when any component is not a finite number.</exception>
    public GeoPoint3D(double x, double y, double z)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            throw new ArgumentException("X must be a finite number.", nameof(x));
        }

        if (double.IsNaN(y) || double.IsInfinity(y))
        {
            throw new ArgumentException("Y must be a finite number.", nameof(y));
        }

        if (double.IsNaN(z) || double.IsInfinity(z))
        {
            throw new ArgumentException("Z must be a finite number.", nameof(z));
        }

        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Gets the X component of the coordinate.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y component of the coordinate.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Gets the Z component of the coordinate.
    /// </summary>
    public double Z { get; }

    /// <inheritdoc />
    public bool Equals(GeoPoint3D other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GeoPoint3D point && Equals(point);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = X.GetHashCode();
            hash = (hash * 397) ^ Y.GetHashCode();
            hash = (hash * 397) ^ Z.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }

    public static bool operator ==(GeoPoint3D left, GeoPoint3D right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GeoPoint3D left, GeoPoint3D right)
    {
        return !left.Equals(right);
    }
}
