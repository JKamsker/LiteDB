#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;

namespace LiteDB.Spatial.Core.Tests.Differential;

/// <summary>
/// Thin wrapper around NetTopologySuite for deterministic oracle comparisons.
/// </summary>
public static class NtsOracle
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), srid: 0);

    public static Polygon CreatePolygon(IEnumerable<GeoPoint> points)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        var coordinates = BuildClosedRing(points);
        return GeometryFactory.CreatePolygon(coordinates);
    }

    public static Polygon CreateAxisAlignedBox(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Only 2D bounding boxes can be mapped to polygons.", nameof(bounds));
        }

        var values = bounds.GetValues();
        var minX = values[0];
        var minY = values[1];
        var maxX = values[2];
        var maxY = values[3];

        var coordinates = new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY)
        };

        return GeometryFactory.CreatePolygon(coordinates);
    }

    public static bool Contains(Polygon polygon, GeoPoint point)
    {
        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        return polygon.Contains(GeometryFactory.CreatePoint(new Coordinate(point.Longitude, point.Latitude)));
    }

    public static bool Covers(Polygon polygon, GeoPoint point)
    {
        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        return polygon.Covers(GeometryFactory.CreatePoint(new Coordinate(point.Longitude, point.Latitude)));
    }

    public static double Distance(GeoPoint left, GeoPoint right)
    {
        var p1 = GeometryFactory.CreatePoint(new Coordinate(left.Longitude, left.Latitude));
        var p2 = GeometryFactory.CreatePoint(new Coordinate(right.Longitude, right.Latitude));
        return DistanceOp.Distance(p1, p2);
    }

    private static Coordinate[] BuildClosedRing(IEnumerable<GeoPoint> points)
    {
        var list = points.ToList();
        if (list.Count < 3)
        {
            throw new ArgumentException("At least three points are required to build a polygon ring.", nameof(points));
        }

        if (!list[0].Equals(list[^1]))
        {
            list.Add(list[0]);
        }

        return list.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray();
    }
}
