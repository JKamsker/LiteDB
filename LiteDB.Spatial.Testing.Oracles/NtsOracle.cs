using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Provides helper methods backed by NetTopologySuite for geographic predicate checks.
/// </summary>
public static class NtsOracle
{
    private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    /// <summary>
    /// Splits a potentially wrapped geographic bounding box into one or more envelopes that do not cross the anti-meridian.
    /// The returned envelopes are clamped to the valid latitude range [-90, 90].
    /// </summary>
    public static IReadOnlyList<Envelope> SplitGeographicBoundingBox((double MinLon, double MinLat, double MaxLon, double MaxLat) bounds)
    {
        var (minLon, minLat, maxLon, maxLat) = bounds;
        var clampedMinLat = Math.Max(minLat, -90d);
        var clampedMaxLat = Math.Min(maxLat, 90d);

        if (double.IsNaN(minLon) || double.IsNaN(maxLon) || double.IsNaN(clampedMinLat) || double.IsNaN(clampedMaxLat))
        {
            throw new ArgumentException("Bounding box coordinates must be finite numbers.");
        }

        if (clampedMaxLat < clampedMinLat)
        {
            throw new ArgumentException("Maximum latitude must be greater than or equal to the minimum latitude.");
        }

        var width = maxLon - minLon;
        if (width >= 360d)
        {
            return new[] { new Envelope(-180d, 180d, clampedMinLat, clampedMaxLat) };
        }

        var normalizedMin = NormalizeLongitude(minLon);
        var normalizedMax = NormalizeLongitude(maxLon);

        if (WrapsAntiMeridian(minLon, maxLon))
        {
            return new[]
            {
                new Envelope(normalizedMin, 180d, clampedMinLat, clampedMaxLat),
                new Envelope(-180d, normalizedMax, clampedMinLat, clampedMaxLat)
            };
        }

        // ensure ordering after normalization
        if (normalizedMax < normalizedMin)
        {
            normalizedMax += 360d;
        }

        return new[] { new Envelope(normalizedMin, normalizedMax, clampedMinLat, clampedMaxLat) };
    }

    /// <summary>
    /// Determines whether a lon/lat coordinate falls inside the provided geographic bounding box, handling wraparound.
    /// </summary>
    public static bool Contains((double MinLon, double MinLat, double MaxLon, double MaxLat) bounds, double longitude, double latitude)
    {
        var envelopes = SplitGeographicBoundingBox(bounds);
        var point = Factory.CreatePoint(new Coordinate(NormalizeLongitude(longitude), ClampLatitude(latitude)));

        foreach (var envelope in envelopes)
        {
            if (envelope.Contains(point.Coordinate))
            {
                return true;
            }
        }

        return false;
    }

    private static double NormalizeLongitude(double value)
    {
        var normalized = value % 360d;
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

    private static double ClampLatitude(double value)
    {
        if (value < -90d)
        {
            return -90d;
        }

        if (value > 90d)
        {
            return 90d;
        }

        return value;
    }

    private static bool WrapsAntiMeridian(double minLon, double maxLon)
    {
        if (maxLon - minLon >= 360d)
        {
            return false;
        }

        if (maxLon > 180d || minLon < -180d)
        {
            return true;
        }

        var normalizedMin = NormalizeLongitude(minLon);
        var normalizedMax = NormalizeLongitude(maxLon);
        return normalizedMax < normalizedMin;
    }
}
