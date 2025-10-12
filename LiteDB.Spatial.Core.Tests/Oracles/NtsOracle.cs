using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial.Core.Tests.TestSupport;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace LiteDB.Spatial.Core.Tests.Oracles;

public static class NtsOracle
{
    private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public static IReadOnlyList<Envelope> SplitBoundingBox(BoundingBoxFixture fixture)
    {
        if (fixture.Bounds.Length != 4)
        {
            throw new ArgumentException("Bounding box fixtures must contain four values (minLon, minLat, maxLon, maxLat).");
        }

        return SplitBoundingBox(fixture.Bounds[0], fixture.Bounds[1], fixture.Bounds[2], fixture.Bounds[3]);
    }

    public static IReadOnlyList<Envelope> SplitBoundingBox(double minLon, double minLat, double maxLon, double maxLat)
    {
        var clampedMinLat = ClampLatitude(minLat);
        var clampedMaxLat = ClampLatitude(maxLat);

        if (maxLon - minLon >= 360d)
        {
            return new[] { new Envelope(-180d, 180d, clampedMinLat, clampedMaxLat) };
        }

        var normalizedMin = NormalizeLongitude(minLon);
        var normalizedMax = NormalizeLongitude(maxLon);

        var spansAntiMeridian = normalizedMax < normalizedMin;

        if (!spansAntiMeridian)
        {
            return new[] { new Envelope(normalizedMin, normalizedMax, clampedMinLat, clampedMaxLat) };
        }

        return new[]
        {
            new Envelope(normalizedMin, 180d, clampedMinLat, clampedMaxLat),
            new Envelope(-180d, normalizedMax, clampedMinLat, clampedMaxLat)
        };
    }

    public static IReadOnlyList<string> WithinBoundingBox(BoundingBoxFixture fixture)
    {
        var envelopes = SplitBoundingBox(fixture);
        var prepared = envelopes
            .Select(CreatePolygon)
            .Select(PreparedGeometryFactory.Prepare)
            .ToArray();

        var matches = new List<string>();
        foreach (var point in fixture.Points)
        {
            var coordinate = new Coordinate(point.Lon, point.Lat);
            var geometry = Factory.CreatePoint(coordinate);

            if (prepared.Any(p => p.Covers(geometry)))
            {
                matches.Add(point.Id);
            }
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static NetTopologySuite.Geometries.Geometry CreatePolygon(Envelope envelope)
    {
        var clamped = new Envelope(
            Math.Max(envelope.MinX, -180d),
            Math.Min(envelope.MaxX, 180d),
            ClampLatitude(envelope.MinY),
            ClampLatitude(envelope.MaxY));

        var coordinates = new[]
        {
            new Coordinate(clamped.MinX, clamped.MinY),
            new Coordinate(clamped.MaxX, clamped.MinY),
            new Coordinate(clamped.MaxX, clamped.MaxY),
            new Coordinate(clamped.MinX, clamped.MaxY),
            new Coordinate(clamped.MinX, clamped.MinY)
        };

        return Factory.CreatePolygon(coordinates);
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
}
