using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes Euclidean distances for two-dimensional Cartesian coordinates.
/// </summary>
public sealed class Euclidean2DDistance : ISpatialDistance
{
    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var dx = left.Longitude - right.Longitude;
        var dy = left.Latitude - right.Latitude;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Euclidean2DDistance supports only two-dimensional points.");
    }
}
