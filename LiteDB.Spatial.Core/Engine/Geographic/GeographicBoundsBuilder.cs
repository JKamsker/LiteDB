#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

internal static class GeographicBoundsBuilder
{
    private const double EarthRadiusMeters = 6378137d;

    public static IReadOnlyList<GeographicBoundsSegment> FromCircle(GeoPoint center, double radiusMeters)
    {
        if (radiusMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Radius must be non-negative.");
        }

        var latDelta = ToDegrees(radiusMeters / EarthRadiusMeters);
        var minLat = ClampLatitude(center.Latitude - latDelta);
        var maxLat = ClampLatitude(center.Latitude + latDelta);

        var segments = BuildLongitudeSegments(center.Longitude, center.Latitude, radiusMeters, minLat, maxLat);
        return segments.Select(segment => CreateSegment(segment.minLon, segment.maxLon, minLat, maxLat)).ToList();
    }

    public static IReadOnlyList<GeographicBoundsSegment> FromBoundingBox(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic bounding boxes must be two-dimensional.", nameof(bounds));
        }

        var values = bounds.GetValues();
        var minLon = values[0];
        var minLat = values[1];
        var maxLon = values[2];
        var maxLat = values[3];

        if (!IsLatitudeWithinRange(minLat) || !IsLatitudeWithinRange(maxLat))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Latitude must remain within [-90, 90] degrees.");
        }

        var segments = SplitLongitudeRange(minLon, maxLon);
        return segments.Select(segment => CreateSegment(segment.min, segment.max, minLat, maxLat)).ToList();
    }

    private static IReadOnlyList<(double minLon, double maxLon)> BuildLongitudeSegments(double centerLon, double centerLat, double radiusMeters, double minLat, double maxLat)
    {
        if (minLat <= -90d || maxLat >= 90d)
        {
            return new[] { (-180d, 180d) };
        }

        var latRadians = ToRadians(centerLat);
        var cosLat = Math.Cos(latRadians);

        if (Math.Abs(cosLat) < 1e-12)
        {
            return new[] { (-180d, 180d) };
        }

        var lonDelta = radiusMeters / (EarthRadiusMeters * cosLat);
        lonDelta = Math.Min(lonDelta, Math.PI);
        var lonDeltaDegrees = ToDegrees(lonDelta);
        var minLon = centerLon - lonDeltaDegrees;
        var maxLon = centerLon + lonDeltaDegrees;

        if (maxLon - minLon >= 360d)
        {
            return new[] { (-180d, 180d) };
        }

        return SplitLongitudeRange(minLon, maxLon);
    }

    private static IReadOnlyList<(double min, double max)> SplitLongitudeRange(double min, double max)
    {
        if (max - min >= 360d)
        {
            return new[] { (-180d, 180d) };
        }

        var normalizedMin = NormalizeLongitude(min);
        var offset = normalizedMin - min;
        var normalizedMax = max + offset;

        if (normalizedMax <= 180d)
        {
            return new[] { (normalizedMin, normalizedMax) };
        }

        return new[]
        {
            (normalizedMin, 180d),
            (-180d, normalizedMax - 360d)
        };
    }

    private static GeographicBoundsSegment CreateSegment(double minLon, double maxLon, double minLat, double maxLat)
    {
        var clampedMinLat = ClampLatitude(minLat);
        var clampedMaxLat = ClampLatitude(maxLat);

        var normalizedMinLon = NormalizeLongitudeToUnit(minLon);
        var normalizedMaxLon = NormalizeLongitudeToUnit(maxLon);
        var normalizedMinLat = NormalizeLatitudeToUnit(clampedMinLat);
        var normalizedMaxLat = NormalizeLatitudeToUnit(clampedMaxLat);

        var normalizedBounds = BoundingBox.From2D(
            Math.Min(normalizedMinLon, normalizedMaxLon),
            Math.Min(normalizedMinLat, normalizedMaxLat),
            Math.Max(normalizedMinLon, normalizedMaxLon),
            Math.Max(normalizedMinLat, normalizedMaxLat));

        return new GeographicBoundsSegment(normalizedBounds, normalizedMinLat, normalizedMaxLat, normalizedMinLon, normalizedMaxLon);
    }

    private static bool IsLatitudeWithinRange(double latitude)
    {
        return latitude >= -90d && latitude <= 90d;
    }

    private static double ClampLatitude(double latitude)
    {
        if (latitude < -90d)
        {
            return -90d;
        }

        if (latitude > 90d)
        {
            return 90d;
        }

        return latitude;
    }

    private static double NormalizeLongitude(double longitude)
    {
        var value = longitude % 360d;
        if (value <= -180d)
        {
            value += 360d;
        }
        else if (value > 180d)
        {
            value -= 360d;
        }

        return value;
    }

    private static double NormalizeLongitudeToUnit(double longitude)
    {
        return (NormalizeLongitude(longitude) + 180d) / 360d;
    }

    private static double NormalizeLatitudeToUnit(double latitude)
    {
        return (ClampLatitude(latitude) + 90d) / 180d;
    }

    private static double ToDegrees(double radians)
    {
        return radians * 180d / Math.PI;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }
}

internal readonly struct GeographicBoundsSegment
{
    public GeographicBoundsSegment(BoundingBox normalized, double minLatNormalized, double maxLatNormalized, double minLonNormalized, double maxLonNormalized)
    {
        Normalized = normalized;
        MinLatitudeNormalized = minLatNormalized;
        MaxLatitudeNormalized = maxLatNormalized;
        MinLongitudeNormalized = minLonNormalized;
        MaxLongitudeNormalized = maxLonNormalized;
    }

    public BoundingBox Normalized { get; }

    public double MinLatitudeNormalized { get; }

    public double MaxLatitudeNormalized { get; }

    public double MinLongitudeNormalized { get; }

    public double MaxLongitudeNormalized { get; }
}
