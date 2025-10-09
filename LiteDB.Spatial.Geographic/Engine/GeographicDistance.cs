using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes geodesic distances between two-dimensional geographic points.
/// </summary>
public sealed class GeographicDistance : ISpatialDistance
{
    /// <summary>
    /// Mean Earth radius in meters according to IUGG.
    /// </summary>
    public const double EarthRadiusMeters = 6_371_008.8d;

    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var lat1 = ToRadians(left.Latitude);
        var lat2 = ToRadians(right.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLon = ToRadians(right.Longitude - left.Longitude);

        var sinHalfLat = Math.Sin(deltaLat / 2d);
        var sinHalfLon = Math.Sin(deltaLon / 2d);

        var a = sinHalfLat * sinHalfLat
            + Math.Cos(lat1) * Math.Cos(lat2) * sinHalfLon * sinHalfLon;

        var clamped = Math.Min(1d, Math.Max(0d, a));
        var c = 2d * Math.Asin(Math.Sqrt(clamped));

        return EarthRadiusMeters * c;
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Geographic distance only supports two-dimensional points.");
    }

    private static double ToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }
}
