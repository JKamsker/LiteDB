using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace LiteDB.Spatial.Core.Tests.Oracles;

internal static class NtsOracle
{
    private static readonly GeometryFactory Factory = GeometryFactory.Default;

    public static bool Contains(IReadOnlyList<(double X, double Y)> ring, (double X, double Y) point)
    {
        var polygon = BuildPolygon(ring);
        var ntsPoint = Factory.CreatePoint(new Coordinate(point.X, point.Y));
        return polygon.Contains(ntsPoint) || polygon.Touches(ntsPoint);
    }

    public static IReadOnlyList<(double X, double Y)> Hull(IReadOnlyList<(double X, double Y)> points)
    {
        var coordinates = points.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        if (coordinates.Length == 0)
        {
            return new List<(double X, double Y)>();
        }

        var geometry = Factory.CreateMultiPointFromCoords(coordinates);
        var hull = geometry.ConvexHull();
        if (hull is Polygon polygon)
        {
            return polygon.Coordinates.Select(c => (c.X, c.Y)).ToList();
        }

        return geometry.ConvexHull().Coordinates.Select(c => (c.X, c.Y)).ToList();
    }

    private static Polygon BuildPolygon(IReadOnlyList<(double X, double Y)> ring)
    {
        var coordinates = ring.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        if (!coordinates.First().Equals2D(coordinates.Last()))
        {
            coordinates = coordinates.Concat(new[] { coordinates.First() }).ToArray();
        }

        var shell = Factory.CreateLinearRing(coordinates);
        return Factory.CreatePolygon(shell);
    }
}
