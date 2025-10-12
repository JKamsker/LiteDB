#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial.Core.Tests.TestSupport;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace LiteDB.Spatial.Core.Tests.Differential;

internal static class NtsOracle
{
    private static readonly GeometryFactory GeometryFactory = new();

    public static Polygon BuildConvexPolygon(IReadOnlyList<GeoPoint> points)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (points.Count < 3)
        {
            throw new ArgumentException("At least three points are required to build a polygon.", nameof(points));
        }

        var coordinates = points.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray();
        var hull = new ConvexHull(coordinates, GeometryFactory).GetConvexHull();

        return hull switch
        {
            Polygon polygon => polygon,
            LineString line => GeometryFactory.CreatePolygon(CreateTriangleFromLine(line)),
            Point point => GeometryFactory.CreatePolygon(CreateTriangleFromPoint(point)),
            _ => throw new InvalidOperationException("Unexpected geometry returned by convex hull computation.")
        };
    }

    public static BoundingBox ToBoundingBox(NtsGeometry geometry)
    {
        if (geometry == null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var envelope = geometry.EnvelopeInternal;
        return BoundingBox.From2D(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }

    public static IReadOnlyCollection<int> PointsWithinBoundingBox(IEnumerable<PointSample> samples, BoundingBox bounds)
    {
        if (samples == null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        var envelope = new Envelope(bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY);
        var polygon = (Polygon)GeometryFactory.ToGeometry(envelope);
        return PointsWithinGeometry(samples, polygon);
    }

    public static IReadOnlyCollection<int> PointsWithinPolygon(IEnumerable<PointSample> samples, NtsGeometry polygon)
    {
        if (samples == null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        return PointsWithinGeometry(samples, polygon);
    }

    private static IReadOnlyCollection<int> PointsWithinGeometry(IEnumerable<PointSample> samples, NtsGeometry geometry)
    {
        var matches = new List<int>();
        foreach (var sample in samples)
        {
            var coordinate = new Coordinate(sample.Position.Longitude, sample.Position.Latitude);
            var point = GeometryFactory.CreatePoint(coordinate);
            if (geometry.Covers(point))
            {
                matches.Add(sample.Id);
            }
        }

        return matches;
    }

    private static Coordinate[] CreateTriangleFromLine(LineString line)
    {
        var start = line.GetCoordinateN(0);
        var end = line.GetCoordinateN(line.NumPoints - 1);
        var mid = new Coordinate((start.X + end.X) / 2d, (start.Y + end.Y) / 2d + 1e-9);
        return new[] { start, end, mid, start };
    }

    private static Coordinate[] CreateTriangleFromPoint(Point point)
    {
        var coord = point.Coordinate;
        return new[]
        {
            new Coordinate(coord.X, coord.Y),
            new Coordinate(coord.X + 1e-9, coord.Y),
            new Coordinate(coord.X, coord.Y + 1e-9),
            new Coordinate(coord.X, coord.Y)
        };
    }
}
