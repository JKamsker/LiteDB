#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes geographic distances using the haversine formula.
/// </summary>
public sealed class GeographicDistance : ISpatialDistance
{
    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var lon1 = GeographicHelpers.DegreesToRadians(left.Longitude);
        var lon2 = GeographicHelpers.DegreesToRadians(right.Longitude);
        var lat1 = GeographicHelpers.DegreesToRadians(left.Latitude);
        var lat2 = GeographicHelpers.DegreesToRadians(right.Latitude);

        var deltaLat = lat2 - lat1;
        var deltaLon = lon2 - lon1;

        var sinLat = Math.Sin(deltaLat / 2d);
        var sinLon = Math.Sin(deltaLon / 2d);

        var a = (sinLat * sinLat) + Math.Cos(lat1) * Math.Cos(lat2) * (sinLon * sinLon);
        var c = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(a)));

        return GeographicHelpers.EarthRadiusMeters * c;
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
