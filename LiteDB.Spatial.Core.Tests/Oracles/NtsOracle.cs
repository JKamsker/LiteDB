#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace LiteDB.Spatial.Core.Tests.Oracles;

internal static class NtsOracle
{
    private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public static IReadOnlyList<string> PointsWithinBoundingBox(BoundingBox bounds, IReadOnlyList<NaturalEarthPoint> points)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Only two-dimensional bounds are supported.", nameof(bounds));
        }

        var polygons = BuildPolygons(bounds);
        var matches = new List<string>();

        foreach (var point in points)
        {
            var coordinate = new Coordinate(point.Longitude, point.Latitude);
            var geometry = Factory.CreatePoint(coordinate);

            if (polygons.Any(polygon => polygon.Covers(geometry)))
            {
                matches.Add(point.Id);
            }
        }

        return matches;
    }

    private static IReadOnlyList<Polygon> BuildPolygons(BoundingBox bounds)
    {
        var segments = SplitLongitudeRange(bounds.MinX, bounds.MaxX);
        var minLat = Math.Max(bounds.MinY, -90d);
        var maxLat = Math.Min(bounds.MaxY, 90d);

        var polygons = new List<Polygon>();

        foreach (var (minLon, maxLon) in segments)
        {
            var orderedMin = Math.Min(minLon, maxLon);
            var orderedMax = Math.Max(minLon, maxLon);

            var shell = new[]
            {
                new Coordinate(orderedMin, minLat),
                new Coordinate(orderedMin, maxLat),
                new Coordinate(orderedMax, maxLat),
                new Coordinate(orderedMax, minLat),
                new Coordinate(orderedMin, minLat)
            };

            polygons.Add(Factory.CreatePolygon(shell));
        }

        if (polygons.Count == 0)
        {
            return Array.Empty<Polygon>();
        }

        return polygons;
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

    internal sealed class NaturalEarthPoint
    {
        public string Id { get; set; } = string.Empty;

        public double Longitude { get; set; }

        public double Latitude { get; set; }
    }
}
