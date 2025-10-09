#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using LiteDB.Spatial;
using NetTopologySuite.Geometries;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

internal static class Cartesian2DGenerators
{
    private const int CoordinateResolution = 1000;

    public static Gen<CartesianBoundingBoxScenario> BoundingBoxes()
    {
        return Gen.Sized(size => GenerateScenario(size + 16));
    }

    public static Gen<CartesianPolygonScenario> ConvexPolygons()
    {
        return Gen.Sized(size => GeneratePolygonScenario(size + 16));
    }

    private static Gen<CartesianBoundingBoxScenario> GenerateScenario(int size)
    {
        return
            from domain in GenerateDomain(size)
            from count in Gen.Choose(Math.Max(6, size / 2), Math.Max(12, size))
            from points in GeneratePoints(domain, count)
            from query in GenerateQuery(domain, points)
            select new CartesianBoundingBoxScenario(domain, query, points);
    }

    private static Gen<CartesianPolygonScenario> GeneratePolygonScenario(int size)
    {
        return
            from domain in GenerateDomain(size)
            from count in Gen.Choose(Math.Max(6, size / 2), Math.Max(12, size))
            from points in GeneratePoints(domain, count)
            from hullSeedCount in Gen.Choose(4, Math.Max(6, size / 4))
            from hullSeeds in GenerateHullSeeds(domain, hullSeedCount)
            let hull = BuildConvexHull(hullSeeds)
            let polygon = NtsOracle.CreatePolygon(hull)
            let envelope = polygon.EnvelopeInternal
            let query = BoundingBox.From2D(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY)
            select new CartesianPolygonScenario(domain, polygon, points, query, hull);
    }

    private static Gen<BoundingBox> GenerateDomain(int size)
    {
        var half = Math.Max(50, size);
        return
            from centerX in Coordinate(-half, half)
            from centerY in Coordinate(-half, half)
            from halfWidth in Coordinate(half / 2d, half)
            from halfHeight in Coordinate(half / 2d, half)
            let minX = centerX - halfWidth
            let maxX = centerX + halfWidth
            let minY = centerY - halfHeight
            let maxY = centerY + halfHeight
            select BoundingBox.From2D(minX, minY, maxX, maxY);
    }

    private static Gen<IReadOnlyList<CartesianPoint>> GeneratePoints(BoundingBox domain, int count)
    {
        var values = domain.GetValues();
        var minX = values[0];
        var minY = values[1];
        var maxX = values[2];
        var maxY = values[3];

        return Gen.ListOf(Coordinate(minX, maxX)
            .SelectMany(x => Coordinate(minY, maxY), (x, y) => new GeoPoint(x, y)), count)
            .Select(points => (IReadOnlyList<CartesianPoint>)points
                .Select((point, index) => new CartesianPoint(index + 1, point))
                .ToList());
    }

    private static Gen<IReadOnlyList<GeoPoint>> GenerateHullSeeds(BoundingBox domain, int count)
    {
        return GeneratePoints(domain, count)
            .Select(list => (IReadOnlyList<GeoPoint>)list.Select(p => p.Position).ToList());
    }

    private static Gen<BoundingBox> GenerateQuery(BoundingBox domain, IReadOnlyList<CartesianPoint> points)
    {
        var values = domain.GetValues();
        var domainMinX = values[0];
        var domainMinY = values[1];
        var domainMaxX = values[2];
        var domainMaxY = values[3];

        var pointGen = Gen.Elements(points.ToArray());

        return
            from first in pointGen
            from second in pointGen
            from expandX in Coordinate(0, Math.Max(0.1, (domainMaxX - domainMinX) * 0.1))
            from expandY in Coordinate(0, Math.Max(0.1, (domainMaxY - domainMinY) * 0.1))
            let rawMinX = Math.Min(first.Position.Longitude, second.Position.Longitude)
            let rawMaxX = Math.Max(first.Position.Longitude, second.Position.Longitude)
            let rawMinY = Math.Min(first.Position.Latitude, second.Position.Latitude)
            let rawMaxY = Math.Max(first.Position.Latitude, second.Position.Latitude)
            let minXCandidate = rawMinX - expandX
            let maxXCandidate = rawMaxX + expandX
            let minYCandidate = rawMinY - expandY
            let maxYCandidate = rawMaxY + expandY
            let adjustedRangeX = AdjustRange(minXCandidate, maxXCandidate, domainMinX, domainMaxX)
            let adjustedRangeY = AdjustRange(minYCandidate, maxYCandidate, domainMinY, domainMaxY)
            select BoundingBox.From2D(adjustedRangeX.Min, adjustedRangeY.Min, adjustedRangeX.Max, adjustedRangeY.Max);
    }

    private const double MinSpan = 0.05;

    private static (double Min, double Max) AdjustRange(double minCandidate, double maxCandidate, double domainMin, double domainMax)
    {
        var clampedMin = Math.Clamp(minCandidate, domainMin, domainMax);
        var clampedMax = Math.Clamp(maxCandidate, domainMin, domainMax);
        var span = Math.Max(clampedMax - clampedMin, MinSpan);

        var min = Math.Max(domainMin, clampedMin);
        var max = Math.Min(domainMax, min + span);

        if (max - min < MinSpan)
        {
            max = Math.Min(domainMax, min + MinSpan);
            min = Math.Max(domainMin, max - MinSpan);
        }

        return (min, max);
    }

    private static Gen<double> Coordinate(double min, double max)
    {
        return from bucket in Gen.Choose(0, CoordinateResolution)
               let fraction = bucket / (double)CoordinateResolution
               select min + ((max - min) * fraction);
    }

    private static IReadOnlyList<GeoPoint> BuildConvexHull(IReadOnlyList<GeoPoint> points)
    {
        var unique = points
            .DistinctBy(p => (Round(p.Longitude), Round(p.Latitude)))
            .OrderBy(p => p.Longitude)
            .ThenBy(p => p.Latitude)
            .ToList();

        if (unique.Count < 3)
        {
            unique.Add(new GeoPoint(unique[0].Longitude + 1, unique[0].Latitude));
            unique.Add(new GeoPoint(unique[0].Longitude, unique[0].Latitude + 1));
        }

        var lower = new List<GeoPoint>();
        foreach (var point in unique)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(point);
        }

        var upper = new List<GeoPoint>();
        for (var i = unique.Count - 1; i >= 0; i--)
        {
            var point = unique[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);

        return lower.Concat(upper).ToList();
    }

    private static double Cross(GeoPoint a, GeoPoint b, GeoPoint c)
    {
        return (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude)
            - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
    }

    private static double Round(double value) => Math.Round(value, 6);
}

internal sealed record CartesianPoint(int Id, GeoPoint Position);

internal sealed record CartesianBoundingBoxScenario(
    BoundingBox Domain,
    BoundingBox Query,
    IReadOnlyList<CartesianPoint> Points);

internal sealed record CartesianPolygonScenario(
    BoundingBox Domain,
    Polygon Polygon,
    IReadOnlyList<CartesianPoint> Points,
    BoundingBox Query,
    IReadOnlyList<GeoPoint> HullPoints);
