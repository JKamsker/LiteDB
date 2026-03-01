using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

internal static class NtsOracle
{
    private static readonly GeometryFactory Geometry = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public static IReadOnlySet<string> WithinBoundingBox(BoundingBox bounds, IReadOnlyList<GeographicFixturePoint> points)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic bounding boxes must be two-dimensional.", nameof(bounds));
        }

        var polygons = BuildBoundingBoxPolygons(bounds).ToList();
        var prepared = polygons.Select(PreparedGeometryFactory.Prepare).ToArray();
        var matches = new HashSet<string>(StringComparer.Ordinal);

        foreach (var point in points)
        {
            var coordinate = new Coordinate(point.Location.Longitude, point.Location.Latitude);
            var geometryPoint = Geometry.CreatePoint(coordinate);

            if (prepared.Any(p => p.Covers(geometryPoint)))
            {
                matches.Add(point.Id);
            }
        }

        return matches;
    }

    private static IEnumerable<Polygon> BuildBoundingBoxPolygons(BoundingBox bounds)
    {
        var minLon = bounds.MinX;
        var maxLon = bounds.MaxX;
        var minLat = ClampLatitude(bounds.MinY);
        var maxLat = ClampLatitude(bounds.MaxY);

        foreach (var segment in SplitLongitudeRange(minLon, maxLon))
        {
            var coordinates = new[]
            {
                new Coordinate(segment.minLon, minLat),
                new Coordinate(segment.maxLon, minLat),
                new Coordinate(segment.maxLon, maxLat),
                new Coordinate(segment.minLon, maxLat),
                new Coordinate(segment.minLon, minLat)
            };

            yield return Geometry.CreatePolygon(coordinates);
        }
    }

    private static IEnumerable<(double minLon, double maxLon)> SplitLongitudeRange(double min, double max)
    {
        if (double.IsNaN(min) || double.IsNaN(max))
        {
            throw new ArgumentException("Longitude values must be finite.");
        }

        if (max - min >= 360d)
        {
            yield return (-180d, 180d);
            yield break;
        }

        var normalizedMin = NormalizeLongitude(min);
        var offset = normalizedMin - min;
        var normalizedMax = max + offset;

        if (normalizedMax <= 180d)
        {
            yield return (normalizedMin, normalizedMax);
            yield break;
        }

        yield return (normalizedMin, 180d);
        yield return (-180d, normalizedMax - 360d);
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
