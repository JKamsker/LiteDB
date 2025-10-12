using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsArb = FsCheck.Fluent.Arb;
using FsGen = FsCheck.Fluent.Gen;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using LiteDB.Spatial.Core.Tests.TestSupport;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

internal static class Cartesian2DGenerators
{
    public static Arbitrary<CartesianPolygonCase> PolygonCases()
    {
        return FsArb.From(GenPolygonCase());
    }

    public static Arbitrary<CartesianBoxCase> BoxCases()
    {
        return FsArb.From(GenBoxCase());
    }

    private static Gen<CartesianPolygonCase> GenPolygonCase()
    {
        return from count in FsGen.Choose(4, 8)
               from raw in FsGen.ListOf(PointGen(), count * 3)
               let unique = raw.Distinct().ToList()
               where unique.Count >= 4
               let hullCoordinates = NtsOracle.Hull(unique.Select(p => (p.X, p.Y)).ToList())
               let hull = NormalizeHull(hullCoordinates)
               where hull.Count >= 4
               let polygonPoints = hull.Take(hull.Count - 1).Select(p => new CartesianPoint2D(p.X, p.Y)).ToList()
               let centroid = ComputeCentroid(polygonPoints)
               let insideSamples = BuildInsideSamples(polygonPoints, centroid)
               let outsideSamples = BuildOutsideSamples(polygonPoints, centroid)
               let candidates = unique.Append(centroid).Concat(insideSamples).Concat(outsideSamples).Distinct().ToList()
               let domain = BuildDomain(polygonPoints.Concat(candidates))
               select new CartesianPolygonCase(domain, candidates, hull.Select(p => new CartesianPoint2D(p.X, p.Y)).ToList(), centroid);
    }

    private static Gen<CartesianBoxCase> GenBoxCase()
    {
        return from count in FsGen.Choose(12, 36)
               from raw in FsGen.ListOf(PointGen(), count * 2)
               let points = raw.Distinct().ToList()
               where points.Count >= 6
               from anchor in FsGen.Elements<CartesianPoint2D>(points)
               from other in FsGen.Elements<CartesianPoint2D>(points)
               let minX = Math.Min(anchor.X, other.X)
               let maxX = Math.Max(anchor.X, other.X)
               let minY = Math.Min(anchor.Y, other.Y)
               let maxY = Math.Max(anchor.Y, other.Y)
               let adjustedMaxX = maxX <= minX ? minX + 0.5 : maxX
               let adjustedMaxY = maxY <= minY ? minY + 0.5 : maxY
               let query = BoundingBox.From2D(minX, minY, adjustedMaxX, adjustedMaxY)
               let domain = BuildDomain(points)
               select new CartesianBoxCase(domain, query, points);
    }

    private static Gen<CartesianPoint2D> PointGen()
    {
        return from x in FsGen.Choose(-1700, 1700)
               from y in FsGen.Choose(-850, 850)
               let scaledX = Math.Round(x / 10.0, 3)
               let scaledY = Math.Round(y / 10.0, 3)
               select new CartesianPoint2D(scaledX, scaledY);
    }

    private static List<(double X, double Y)> NormalizeHull(IReadOnlyList<(double X, double Y)> hull)
    {
        if (hull.Count == 0)
        {
            return new List<(double X, double Y)>();
        }

        var closed = hull.ToList();
        if (!PointsEqual(closed[0], closed[^1]))
        {
            closed.Add(closed[0]);
        }

        return closed;
    }

    private static bool PointsEqual((double X, double Y) left, (double X, double Y) right)
    {
        return Math.Abs(left.X - right.X) < 1e-9 && Math.Abs(left.Y - right.Y) < 1e-9;
    }

    private static CartesianPoint2D ComputeCentroid(IReadOnlyList<CartesianPoint2D> points)
    {
        var count = points.Count;
        var sumX = points.Sum(p => p.X);
        var sumY = points.Sum(p => p.Y);
        return new CartesianPoint2D(sumX / count, sumY / count);
    }

    private static IEnumerable<CartesianPoint2D> BuildInsideSamples(IReadOnlyList<CartesianPoint2D> hull, CartesianPoint2D centroid)
    {
        foreach (var vertex in hull)
        {
            var inside = new CartesianPoint2D((vertex.X + centroid.X) / 2d, (vertex.Y + centroid.Y) / 2d);
            yield return inside;
        }
    }

    private static IEnumerable<CartesianPoint2D> BuildOutsideSamples(IReadOnlyList<CartesianPoint2D> hull, CartesianPoint2D centroid)
    {
        foreach (var vertex in hull)
        {
            var dx = vertex.X - centroid.X;
            var dy = vertex.Y - centroid.Y;
            var outside = ClampPoint(new CartesianPoint2D(vertex.X + dx, vertex.Y + dy));
            if (!NtsOracle.Contains(hull.Select(p => (p.X, p.Y)).ToList(), (outside.X, outside.Y)))
            {
                yield return outside;
            }
        }
    }

    private static CartesianPoint2D ClampPoint(CartesianPoint2D point)
    {
        var clampedX = Math.Max(-179.9, Math.Min(179.9, point.X));
        var clampedY = Math.Max(-89.9, Math.Min(89.9, point.Y));
        return new CartesianPoint2D(clampedX, clampedY);
    }

    private static BoundingBox BuildDomain(IEnumerable<CartesianPoint2D> points)
    {
        var list = points.ToList();
        var minX = list.Min(p => p.X) - 1;
        var maxX = list.Max(p => p.X) + 1;
        var minY = list.Min(p => p.Y) - 1;
        var maxY = list.Max(p => p.Y) + 1;

        minX = Math.Max(minX, -180);
        maxX = Math.Min(maxX, 180);
        minY = Math.Max(minY, -90);
        maxY = Math.Min(maxY, 90);

        if (maxX <= minX)
        {
            maxX = minX + 1;
        }

        if (maxY <= minY)
        {
            maxY = minY + 1;
        }

        return BoundingBox.From2D(minX, minY, maxX, maxY);
    }
}

public sealed record CartesianPolygonCase(
    BoundingBox Domain,
    IReadOnlyList<CartesianPoint2D> Candidates,
    IReadOnlyList<CartesianPoint2D> Polygon,
    CartesianPoint2D Centroid)
{
    public IReadOnlyList<(double X, double Y)> ToRing() => Polygon.Select(p => (p.X, p.Y)).ToList();
}

public sealed record CartesianBoxCase(BoundingBox Domain, BoundingBox Query, IReadOnlyList<CartesianPoint2D> Points);
