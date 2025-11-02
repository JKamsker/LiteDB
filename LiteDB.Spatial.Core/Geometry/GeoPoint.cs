#nullable enable

using System;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a geographic coordinate using longitude and latitude expressed in decimal degrees.
/// </summary>
public readonly struct GeoPoint : IEquatable<GeoPoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPoint"/> struct.
    /// </summary>
    /// <param name="longitude">The longitude component in decimal degrees.</param>
    /// <param name="latitude">The latitude component in decimal degrees.</param>
    /// <exception cref="ArgumentException">Thrown when either coordinate is not a finite number.</exception>
    public GeoPoint(double longitude, double latitude)
    {
        if (double.IsNaN(longitude) || double.IsInfinity(longitude))
        {
            throw new ArgumentException("Longitude must be a finite number.", nameof(longitude));
        }

        if (double.IsNaN(latitude) || double.IsInfinity(latitude))
        {
            throw new ArgumentException("Latitude must be a finite number.", nameof(latitude));
        }

        Longitude = longitude;
        Latitude = latitude;
    }

    /// <summary>
    /// Gets the longitude component in decimal degrees where positive values indicate east.
    /// </summary>
    public double Longitude { get; }

    /// <summary>
    /// Gets the latitude component in decimal degrees where positive values indicate north.
    /// </summary>
    public double Latitude { get; }

    /// <inheritdoc />
    public bool Equals(GeoPoint other)
    {
        return Longitude.Equals(other.Longitude) && Latitude.Equals(other.Latitude);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GeoPoint point && Equals(point);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Longitude.GetHashCode();
            hash = (hash * 397) ^ Latitude.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1})", Longitude, Latitude);
    }

    public static bool operator ==(GeoPoint left, GeoPoint right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GeoPoint left, GeoPoint right)
    {
        return !left.Equals(right);
    }
}
