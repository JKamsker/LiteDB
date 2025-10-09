#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

internal static class GeographicMath
{
    private const double DegToRad = Math.PI / 180d;
    private const double RadToDeg = 180d / Math.PI;
    private const double EarthRadiusMeters = 6_378_137d;
    private const double PolarClamp = 90d - 1e-9;

    public static GeoPoint NormalizePoint(GeoPoint point)
    {
        var longitude = NormalizeLongitude(point.Longitude);
        var latitude = ClampLatitude(point.Latitude);
        return new GeoPoint(longitude, latitude);
    }

    public static double NormalizeLongitude(double longitude)
    {
        var wrapped = longitude % 360d;
        if (wrapped > 180d)
        {
            wrapped -= 360d;
        }
        else if (wrapped < -180d)
        {
            wrapped += 360d;
        }

        return wrapped;
    }

    public static double ClampLatitude(double latitude)
    {
        if (latitude > PolarClamp)
        {
            return PolarClamp;
        }

        if (latitude < -PolarClamp)
        {
            return -PolarClamp;
        }

        return latitude;
    }

    public static double ToNormalizedLongitude(double longitude)
    {
        return (NormalizeLongitude(longitude) + 180d) / 360d;
    }

    public static double ToNormalizedLatitude(double latitude)
    {
        return (ClampLatitude(latitude) + 90d) / 180d;
    }

    public static IReadOnlyList<BoundingBox> CreateBoundingBoxesForCircle(GeoPoint center, double radiusMeters)
    {
        if (radiusMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Radius must be non-negative.");
        }

        var normalizedCenter = NormalizePoint(center);
        var latRadius = radiusMeters / EarthRadiusMeters * RadToDeg;
        var minLat = ClampLatitude(normalizedCenter.Latitude - latRadius);
        var maxLat = ClampLatitude(normalizedCenter.Latitude + latRadius);

        double minLon;
        double maxLon;

        if (Math.Abs(maxLat) >= PolarClamp || Math.Abs(minLat) >= PolarClamp)
        {
            minLon = -180d;
            maxLon = 180d;
        }
        else
        {
            var latRad = normalizedCenter.Latitude * DegToRad;
            var cosLat = Math.Cos(latRad);
            if (Math.Abs(cosLat) < 1e-12)
            {
                minLon = -180d;
                maxLon = 180d;
            }
            else
            {
                var lonRadius = radiusMeters / (EarthRadiusMeters * cosLat) * RadToDeg;
                if (lonRadius >= 180d)
                {
                    minLon = -180d;
                    maxLon = 180d;
                }
                else
                {
                    minLon = NormalizeLongitude(normalizedCenter.Longitude - lonRadius);
                    maxLon = NormalizeLongitude(normalizedCenter.Longitude + lonRadius);
                }
            }
        }

        return SplitAntimeridian(minLon, maxLon, minLat, maxLat);
    }

    public static IReadOnlyList<BoundingBox> NormalizeBoundingBox(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic bounding boxes must be two-dimensional.", nameof(bounds));
        }

        var minLat = ClampLatitude(bounds.MinY);
        var maxLat = ClampLatitude(bounds.MaxY);

        var minLon = NormalizeLongitude(bounds.MinX);
        var maxLon = NormalizeLongitude(bounds.MaxX);

        return SplitAntimeridian(minLon, maxLon, minLat, maxLat);
    }

    public static BoundingBox ToNormalizedBox(BoundingBox box)
    {
        if (box.Dimensions != 2)
        {
            throw new ArgumentException("Geographic bounding boxes must be two-dimensional.");
        }

        return BoundingBox.From2D(
            ToNormalizedLongitude(box.MinX),
            ToNormalizedLatitude(box.MinY),
            ToNormalizedLongitude(box.MaxX),
            ToNormalizedLatitude(box.MaxY));
    }

    public static BoundingBox NormalizeToUnitBounds(BoundingBox box)
    {
        if (box.Dimensions != 2)
        {
            throw new ArgumentException("Geographic bounding boxes must be two-dimensional.");
        }

        var minX = Math.Max(0d, Math.Min(1d, box.MinX));
        var minY = Math.Max(0d, Math.Min(1d, box.MinY));
        var maxX = Math.Max(minX, Math.Max(0d, Math.Min(1d, box.MaxX)));
        var maxY = Math.Max(minY, Math.Max(0d, Math.Min(1d, box.MaxY)));
        return BoundingBox.From2D(minX, minY, maxX, maxY);
    }

    private static IReadOnlyList<BoundingBox> SplitAntimeridian(double minLon, double maxLon, double minLat, double maxLat)
    {
        minLat = ClampLatitude(minLat);
        maxLat = ClampLatitude(maxLat);

        if (minLon <= maxLon)
        {
            return new[] { BoundingBox.From2D(minLon, minLat, maxLon, maxLat) };
        }

        return new[]
        {
            BoundingBox.From2D(minLon, minLat, 180d, maxLat),
            BoundingBox.From2D(-180d, minLat, maxLon, maxLat)
        };
    }
}
