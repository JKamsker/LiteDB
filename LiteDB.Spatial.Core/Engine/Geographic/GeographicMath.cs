#nullable enable

using System;

namespace LiteDB.Spatial;

internal static class GeographicMath
{
    internal const double EarthRadiusMeters = 6_371_000d;
    private const double DegToRad = Math.PI / 180d;
    private const double RadToDeg = 180d / Math.PI;

    internal static double ClampLatitude(double latitude)
    {
        if (double.IsNaN(latitude))
        {
            return latitude;
        }

        if (latitude > 90d)
        {
            return 90d;
        }

        if (latitude < -90d)
        {
            return -90d;
        }

        return latitude;
    }

    internal static double NormalizeLongitude(double longitude)
    {
        if (double.IsNaN(longitude))
        {
            return longitude;
        }

        var normalized = longitude % 360d;

        if (normalized <= -180d)
        {
            normalized += 360d;
        }
        else if (normalized > 180d)
        {
            normalized -= 360d;
        }

        return normalized;
    }

    internal static double ToRadians(double degrees) => degrees * DegToRad;

    internal static double ToDegrees(double radians) => radians * RadToDeg;

    internal static double LongitudeToUnit(double longitude)
    {
        var normalized = NormalizeLongitude(longitude);
        return (normalized + 180d) / 360d;
    }

    internal static double LatitudeToUnit(double latitude)
    {
        var clamped = ClampLatitude(latitude);
        return (clamped + 90d) / 180d;
    }

    internal static double HaversineDistance(GeoPoint left, GeoPoint right)
    {
        return HaversineDistance(left.Longitude, left.Latitude, right.Longitude, right.Latitude);
    }

    internal static double HaversineDistance(double lon1, double lat1, double lon2, double lat2)
    {
        var lat1Rad = ToRadians(lat1);
        var lat2Rad = ToRadians(lat2);
        var dLat = lat2Rad - lat1Rad;
        var dLon = ToRadians(NormalizeLongitude(lon2 - lon1));

        var sinLat = Math.Sin(dLat / 2d);
        var sinLon = Math.Sin(dLon / 2d);
        var cosLat1 = Math.Cos(lat1Rad);
        var cosLat2 = Math.Cos(lat2Rad);

        var hav = sinLat * sinLat + cosLat1 * cosLat2 * sinLon * sinLon;
        hav = Math.Min(1d, Math.Max(0d, hav));

        var c = 2d * Math.Atan2(Math.Sqrt(hav), Math.Sqrt(Math.Max(0d, 1d - hav)));
        return EarthRadiusMeters * c;
    }
}
