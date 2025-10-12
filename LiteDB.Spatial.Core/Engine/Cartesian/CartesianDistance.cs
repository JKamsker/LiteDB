#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Implements Euclidean distance calculations for Cartesian coordinates.
/// </summary>
public sealed class CartesianDistance : ISpatialDistance
{
    private readonly bool _supports3D;

    public CartesianDistance(bool supports3D)
    {
        _supports3D = supports3D;
    }

    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var dx = left.Longitude - right.Longitude;
        var dy = left.Latitude - right.Latitude;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        if (!_supports3D)
        {
            throw new NotSupportedException("The Cartesian distance calculator is configured for 2D geometry.");
        }

        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
