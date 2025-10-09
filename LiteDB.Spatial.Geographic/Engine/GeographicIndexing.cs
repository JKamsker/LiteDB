using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers for geographic indexing and wrap-around aware coverings.
/// </summary>
internal static class GeographicIndexing
{
    private const double DegreesToRadians = Math.PI / 180d;
    private const double RadiansToDegrees = 180d / Math.PI;

    /// <summary>
    /// Normalizes a longitude expressed in degrees to the [0,1] range used by the Morton encoder.
    /// </summary>
    public static double NormalizeLongitude(double longitude)
    {
        var normalized = NormalizeLongitudeDegrees(longitude);
        return (normalized + 180d) / 360d;
    }

    /// <summary>
    /// Normalizes a latitude expressed in degrees to the [0,1] range used by the Morton encoder.
    /// </summary>
    public static double NormalizeLatitude(double latitude)
    {
        var clamped = Math.Max(-90d, Math.Min(90d, latitude));
        return (clamped + 90d) / 180d;
    }

    /// <summary>
    /// Builds a near query covering using geodesic math while accounting for wrap-around and poles.
    /// </summary>
    /// <param name="center">The circle center in decimal degrees.</param>
    /// <param name="radiusMeters">The radius in meters.</param>
    /// <returns>A covering describing the coarse bounds to evaluate.</returns>
    public static GeographicCover BuildNearCover(GeoPoint center, double radiusMeters)
    {
        if (radiusMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Radius must be non-negative.");
        }

        if (radiusMeters == 0d)
        {
            var bbox = BoundingBox.From2D(center.Longitude, center.Latitude, center.Longitude, center.Latitude);
            return new GeographicCover(bbox, new[] { bbox });
        }

        var angularDistance = radiusMeters / GeographicDistance.EarthRadiusMeters;

        var centerLatRad = center.Latitude * DegreesToRadians;
        var centerLonRad = center.Longitude * DegreesToRadians;

        var minLat = centerLatRad - angularDistance;
        var maxLat = centerLatRad + angularDistance;

        minLat = Math.Max(minLat, -Math.PI / 2d);
        maxLat = Math.Min(maxLat, Math.PI / 2d);

        double minLon;
        double maxLon;

        if (maxLat >= Math.PI / 2d || minLat <= -Math.PI / 2d)
        {
            // Circles touching the poles span the entire longitude range.
            minLon = -Math.PI;
            maxLon = Math.PI;
        }
        else
        {
            var deltaLon = Math.Asin(Math.Sin(angularDistance) / Math.Cos(centerLatRad));
            minLon = centerLonRad - deltaLon;
            maxLon = centerLonRad + deltaLon;

            // Normalise so min <= max in the unwrapped domain.
            if (maxLon - minLon >= 2d * Math.PI)
            {
                minLon = -Math.PI;
                maxLon = Math.PI;
            }
        }

        var minLatDeg = minLat * RadiansToDegrees;
        var maxLatDeg = maxLat * RadiansToDegrees;
        var rawMinLonDeg = minLon * RadiansToDegrees;
        var rawMaxLonDeg = maxLon * RadiansToDegrees;

        var segments = BuildLongitudeSegments(rawMinLonDeg, rawMaxLonDeg, minLatDeg, maxLatDeg);
        var covering = MergeSegments(segments);
        return covering;
    }

    /// <summary>
    /// Converts a geographic bounding box into a normalized representation suitable for Morton encoding.
    /// </summary>
    public static BoundingBox NormalizeBoundingBox(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Only 2D bounding boxes can be normalized for the geographic encoder.", nameof(bounds));
        }

        var values = bounds.ToArray();
        values[0] = NormalizeLongitude(values[0]);
        values[1] = NormalizeLatitude(values[1]);
        values[2] = NormalizeLongitude(values[2]);
        values[3] = NormalizeLatitude(values[3]);
        return BoundingBox.Create(values);
    }

    private static double NormalizeLongitudeDegrees(double longitude)
    {
        var result = longitude;
        while (result < -180d)
        {
            result += 360d;
        }

        while (result > 180d)
        {
            result -= 360d;
        }

        return result;
    }

    private static GeographicCover MergeSegments(List<BoundingBox> segments)
    {
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("At least one segment must be produced for a geographic covering.");
        }

        if (segments.Count == 1)
        {
            return new GeographicCover(segments[0], segments);
        }

        var latMin = segments.Min(s => s.MinY);
        var latMax = segments.Max(s => s.MaxY);
        var bounds = BoundingBox.From2D(-180d, latMin, 180d, latMax);
        return new GeographicCover(bounds, segments);
    }

    private static List<BoundingBox> BuildLongitudeSegments(double rawMinLon, double rawMaxLon, double minLat, double maxLat)
    {
        var normalizedMin = NormalizeLongitudeDegrees(rawMinLon);
        var normalizedMax = NormalizeLongitudeDegrees(rawMaxLon);

        if (rawMaxLon - rawMinLon >= 360d - 1e-9)
        {
            return new List<BoundingBox> { BoundingBox.From2D(-180d, minLat, 180d, maxLat) };
        }

        if (normalizedMax >= normalizedMin)
        {
            return new List<BoundingBox> { BoundingBox.From2D(normalizedMin, minLat, normalizedMax, maxLat) };
        }

        // Wrap-around: split into two segments.
        var first = BoundingBox.From2D(normalizedMin, minLat, 180d, maxLat);
        var second = BoundingBox.From2D(-180d, minLat, normalizedMax, maxLat);
        return new List<BoundingBox> { first, second };
    }
}

/// <summary>
/// Represents a covering for a geographic query and its associated bounding boxes.
/// </summary>
internal readonly struct GeographicCover
{
    public GeographicCover(BoundingBox coveringBounds, IReadOnlyList<BoundingBox> segments)
    {
        CoveringBounds = coveringBounds;
        Segments = segments;
    }

    /// <summary>
    /// Gets the coarse bounding box applied before exact predicates.
    /// </summary>
    public BoundingBox CoveringBounds { get; }

    /// <summary>
    /// Gets the longitude segments that should be individually converted to Morton ranges.
    /// </summary>
    public IReadOnlyList<BoundingBox> Segments { get; }
}
