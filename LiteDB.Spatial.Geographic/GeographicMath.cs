#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Provides math helpers for geographic (longitude/latitude) computations.
/// </summary>
internal static class GeographicMath
{
    public const double EarthRadiusMeters = 6_378_137d;
    private const double HalfPi = Math.PI / 2d;

    public static double NormalizeLongitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Longitude must be a finite value.", nameof(value));
        }

        var normalized = (value + 180d) % 360d;
        if (normalized < 0d)
        {
            normalized += 360d;
        }

        normalized -= 180d;

        if (normalized == -180d && value > 0d)
        {
            return 180d;
        }

        return normalized;
    }

    public static double NormalizeLatitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Latitude must be a finite value.", nameof(value));
        }

        if (value > 90d)
        {
            return 90d;
        }

        if (value < -90d)
        {
            return -90d;
        }

        return value;
    }

    public static double LongitudeToUnit(double longitude)
    {
        return (NormalizeLongitude(longitude) + 180d) / 360d;
    }

    public static double LatitudeToUnit(double latitude)
    {
        return (NormalizeLatitude(latitude) + 90d) / 180d;
    }

    public static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }

    public static double RadiansToDegrees(double radians)
    {
        return radians * 180d / Math.PI;
    }

    public static double HaversineDistance(GeoPoint left, GeoPoint right)
    {
        var lat1 = DegreesToRadians(left.Latitude);
        var lat2 = DegreesToRadians(right.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLon = DegreesToRadians(right.Longitude - left.Longitude);

        var sinLat = Math.Sin(deltaLat / 2d);
        var sinLon = Math.Sin(deltaLon / 2d);
        var a = (sinLat * sinLat) + (Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0d, 1d - a)));
        return EarthRadiusMeters * c;
    }

    public static IReadOnlyList<BoundingBox> BuildCircleCovering(GeoPoint center, double radiusMeters)
    {
        var latitude = NormalizeLatitude(center.Latitude);
        var longitude = NormalizeLongitude(center.Longitude);
        var angularDistance = radiusMeters / EarthRadiusMeters;

        var latRad = DegreesToRadians(latitude);
        var minLat = latRad - angularDistance;
        var maxLat = latRad + angularDistance;

        minLat = Math.Max(minLat, -HalfPi);
        maxLat = Math.Min(maxLat, HalfPi);

        double minLon;
        double maxLon;

        if (minLat <= -HalfPi || maxLat >= HalfPi)
        {
            minLon = -Math.PI;
            maxLon = Math.PI;
        }
        else
        {
            var deltaLon = Math.Asin(Math.Sin(angularDistance) / Math.Cos(latRad));
            if (double.IsNaN(deltaLon) || double.IsInfinity(deltaLon))
            {
                deltaLon = Math.PI;
            }

            minLon = DegreesToRadians(longitude) - deltaLon;
            maxLon = DegreesToRadians(longitude) + deltaLon;
        }

        var minLatDeg = RadiansToDegrees(minLat);
        var maxLatDeg = RadiansToDegrees(maxLat);

        var spans = SplitLongitudeRange(RadiansToDegrees(minLon), RadiansToDegrees(maxLon));
        var boxes = new List<BoundingBox>(spans.Count);

        foreach (var span in spans)
        {
            boxes.Add(BoundingBox.From2D(span.min, minLatDeg, span.max, maxLatDeg));
        }

        return boxes;
    }

    public static IReadOnlyList<BoundingBox> BuildBoundsCovering(BoundingBox bounds)
    {
        var minLat = NormalizeLatitude(bounds.MinY);
        var maxLat = NormalizeLatitude(bounds.MaxY);

        if (maxLat < minLat)
        {
            (minLat, maxLat) = (maxLat, minLat);
        }

        var spans = SplitLongitudeRange(bounds.MinX, bounds.MaxX);
        var boxes = new List<BoundingBox>(spans.Count);

        foreach (var span in spans)
        {
            boxes.Add(BoundingBox.From2D(span.min, minLat, span.max, maxLat));
        }

        return boxes;
    }

    private static IReadOnlyList<(double min, double max)> SplitLongitudeRange(double minLon, double maxLon)
    {
        if (double.IsNaN(minLon) || double.IsInfinity(minLon) || double.IsNaN(maxLon) || double.IsInfinity(maxLon))
        {
            throw new ArgumentException("Longitude bounds must be finite values.");
        }

        var width = maxLon - minLon;

        if (width >= 360d)
        {
            return new[] { (-180d, 180d) };
        }

        var normalizedMin = NormalizeLongitude(minLon);
        var normalizedMax = NormalizeLongitude(maxLon);

        if (normalizedMin <= normalizedMax && width <= 360d)
        {
            return new[] { (normalizedMin, normalizedMax) };
        }

        return new[]
        {
            (normalizedMin, 180d),
            (-180d, normalizedMax)
        };
    }
}
