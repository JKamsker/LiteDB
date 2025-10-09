#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

internal static class GeographicBoundsBuilder
{
    private const double LongitudinalFullSpan = 360d - 1e-9;

    internal static IReadOnlyList<BoundingBox> CircleToBoundingBoxes(GeoPoint center, double radiusMeters)
    {
        if (radiusMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        }

        if (radiusMeters == 0d)
        {
            var longitude = GeographicMath.NormalizeLongitude(center.Longitude);
            var latitude = GeographicMath.ClampLatitude(center.Latitude);
            return new[] { BoundingBox.From2D(longitude, latitude, longitude, latitude) };
        }

        var angularDistance = radiusMeters / GeographicMath.EarthRadiusMeters;
        var centerLatRadians = GeographicMath.ToRadians(center.Latitude);
        var minLatRadians = centerLatRadians - angularDistance;
        var maxLatRadians = centerLatRadians + angularDistance;

        var minLat = GeographicMath.ClampLatitude(GeographicMath.ToDegrees(minLatRadians));
        var maxLat = GeographicMath.ClampLatitude(GeographicMath.ToDegrees(maxLatRadians));

        if (minLat <= -90d || maxLat >= 90d)
        {
            return new[] { BoundingBox.From2D(-180d, minLat, 180d, maxLat) };
        }

        var cosLat = Math.Cos(centerLatRadians);
        if (Math.Abs(cosLat) < 1e-12)
        {
            return new[] { BoundingBox.From2D(-180d, minLat, 180d, maxLat) };
        }

        var sinAngular = Math.Sin(angularDistance);
        var ratio = Math.Min(1d, Math.Max(-1d, sinAngular / cosLat));
        var deltaLon = GeographicMath.ToDegrees(Math.Asin(ratio));

        var minLon = center.Longitude - deltaLon;
        var maxLon = center.Longitude + deltaLon;

        return BuildLongitudeSegments(minLon, maxLon, minLat, maxLat);
    }

    internal static IReadOnlyList<BoundingBox> SplitBoundingBox(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Expected a 2D bounding box for geographic queries.", nameof(bounds));
        }

        var minLat = GeographicMath.ClampLatitude(bounds.MinY);
        var maxLat = GeographicMath.ClampLatitude(bounds.MaxY);

        return BuildLongitudeSegments(bounds.MinX, bounds.MaxX, minLat, maxLat);
    }

    internal static BoundingBox CombineForCovering(IReadOnlyList<BoundingBox> boxes)
    {
        if (boxes.Count == 0)
        {
            throw new ArgumentException("At least one bounding box is required to compute a covering region.", nameof(boxes));
        }

        var minLat = boxes.Min(b => b.MinY);
        var maxLat = boxes.Max(b => b.MaxY);
        var minLon = boxes.Min(b => b.MinX);
        var maxLon = boxes.Max(b => b.MaxX);

        return BoundingBox.From2D(minLon, minLat, maxLon, maxLat);
    }

    internal static IReadOnlyList<SpatialIndexRange> CoverWithEncoder(
        IReadOnlyList<BoundingBox> boxes,
        ISpatialIndexEncoder encoder,
        int maxCells)
    {
        var ranges = new List<SpatialIndexRange>();
        foreach (var box in boxes)
        {
            var normalized = NormalizeToUnit(box);
            ranges.AddRange(encoder.Cover(normalized, maxCells));
        }

        if (ranges.Count == 0)
        {
            return Array.Empty<SpatialIndexRange>();
        }

        return MortonIndexEncoder.UnionAdjacentRanges(ranges);
    }

    private static IReadOnlyList<BoundingBox> BuildLongitudeSegments(double minLon, double maxLon, double minLat, double maxLat)
    {
        var span = maxLon - minLon;
        if (span >= LongitudinalFullSpan)
        {
            var normalizedMinLat = Math.Min(minLat, maxLat);
            var normalizedMaxLat = Math.Max(minLat, maxLat);
            return new[] { BoundingBox.From2D(-180d, normalizedMinLat, 180d, normalizedMaxLat) };
        }

        var normalizedMin = GeographicMath.NormalizeLongitude(minLon);
        var normalizedMax = normalizedMin + span;

        if (normalizedMax <= 180d)
        {
            return new[] { BoundingBox.From2D(normalizedMin, Math.Min(minLat, maxLat), Math.Min(180d, normalizedMax), Math.Max(minLat, maxLat)) };
        }

        var first = BoundingBox.From2D(normalizedMin, Math.Min(minLat, maxLat), 180d, Math.Max(minLat, maxLat));
        var secondEnd = normalizedMax - 360d;
        var second = BoundingBox.From2D(-180d, Math.Min(minLat, maxLat), secondEnd, Math.Max(minLat, maxLat));

        if (second.MaxX <= second.MinX)
        {
            return new[] { BoundingBox.From2D(-180d, Math.Min(minLat, maxLat), 180d, Math.Max(minLat, maxLat)) };
        }

        return new[] { first, second };
    }

    private static BoundingBox NormalizeToUnit(BoundingBox degreesBox)
    {
        var minLon = GeographicMath.NormalizeLongitude(degreesBox.MinX);
        var maxLon = GeographicMath.NormalizeLongitude(degreesBox.MaxX);

        if (maxLon < minLon)
        {
            maxLon += 360d;
        }

        var span = maxLon - minLon;
        if (span >= LongitudinalFullSpan)
        {
            minLon = -180d;
            maxLon = 180d;
        }

        var minLat = GeographicMath.ClampLatitude(degreesBox.MinY);
        var maxLat = GeographicMath.ClampLatitude(degreesBox.MaxY);

        var minX = GeographicMath.LongitudeToUnit(minLon);
        var maxX = GeographicMath.LongitudeToUnit(maxLon);

        if (span >= LongitudinalFullSpan)
        {
            minX = 0d;
            maxX = 1d;
        }
        else if (maxX < minX)
        {
            maxX += 1d;
        }

        var minY = GeographicMath.LatitudeToUnit(minLat);
        var maxY = GeographicMath.LatitudeToUnit(maxLat);

        return BoundingBox.From2D(minX, minY, maxX, maxY);
    }
}
