using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Support;

public static class CartesianScenarioFactory
{
    public static PolygonScenario CreatePolygonScenario(Random random)
    {
        var vertexCount = random.Next(3, 9);
        var centerX = NextDouble(random, -50, 50);
        var centerY = NextDouble(random, -50, 50);
        var baseRadius = NextDouble(random, 2, 25);

        var angles = Enumerable.Range(0, vertexCount)
            .Select(_ => NextDouble(random, 0, Math.PI * 2))
            .OrderBy(x => x)
            .ToArray();

        var ring = new List<GeoPoint>(vertexCount + 1);
        foreach (var angle in angles)
        {
            var radius = baseRadius * (0.5 + NextDouble(random, 0, 0.5));
            var x = centerX + Math.Cos(angle) * radius;
            var y = centerY + Math.Sin(angle) * radius;
            ring.Add(new GeoPoint(x, y));
        }

        ring.Add(ring[0]);

        var polygon = new GeoPolygon(ring);
        var bounds = ComputeBounds(ring);
        var domain = Expand(bounds, baseRadius * 0.25);
        var samples = GenerateSamples(random, domain, polygon, 64);

        return new PolygonScenario(domain, polygon, samples);
    }

    public static BoundingBoxScenario CreateBoundingBoxScenario(Random random)
    {
        var minX = NextDouble(random, -100, 0);
        var minY = NextDouble(random, -100, 0);
        var width = NextDouble(random, 5, 80);
        var height = NextDouble(random, 5, 80);
        var bounds = BoundingBox.From2D(minX, minY, minX + width, minY + height);
        var domain = Expand(bounds, Math.Max(width, height) * 0.5);
        var samples = GenerateSamples(random, domain, null, 64);
        return new BoundingBoxScenario(domain, bounds, samples);
    }

    private static BoundingBox ComputeBounds(IReadOnlyList<GeoPoint> ring)
    {
        var minX = ring.Min(p => p.Longitude);
        var minY = ring.Min(p => p.Latitude);
        var maxX = ring.Max(p => p.Longitude);
        var maxY = ring.Max(p => p.Latitude);
        return BoundingBox.From2D(minX, minY, maxX, maxY);
    }

    private static BoundingBox Expand(BoundingBox bounds, double padding)
    {
        var minX = bounds.MinX - padding;
        var minY = bounds.MinY - padding;
        var maxX = bounds.MaxX + padding;
        var maxY = bounds.MaxY + padding;
        return BoundingBox.From2D(minX, minY, maxX, maxY);
    }

    private static List<GeoPoint> GenerateSamples(Random random, BoundingBox domain, GeoPolygon? polygon, int count)
    {
        var samples = new List<GeoPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var x = NextDouble(random, domain.MinX, domain.MaxX);
            var y = NextDouble(random, domain.MinY, domain.MaxY);
            samples.Add(new GeoPoint(x, y));
        }

        if (polygon != null)
        {
            var vertices = polygon.Outer.Take(polygon.Outer.Count - 1).ToArray();
            for (var i = 0; i < count / 4; i++)
            {
                var point = RandomPointInside(random, vertices);
                samples.Add(point);
            }
        }

        return samples;
    }

    private static GeoPoint RandomPointInside(Random random, IReadOnlyList<GeoPoint> vertices)
    {
        var weights = new double[vertices.Count];
        var total = 0d;
        for (var i = 0; i < vertices.Count; i++)
        {
            var weight = random.NextDouble();
            weights[i] = weight;
            total += weight;
        }

        if (total == 0)
        {
            return vertices[0];
        }

        var x = 0d;
        var y = 0d;
        for (var i = 0; i < vertices.Count; i++)
        {
            var factor = weights[i] / total;
            x += vertices[i].Longitude * factor;
            y += vertices[i].Latitude * factor;
        }

        return new GeoPoint(x, y);
    }

    private static double NextDouble(Random random, double min, double max)
    {
        return min + (random.NextDouble() * (max - min));
    }
}

public sealed record PolygonScenario(BoundingBox Domain, GeoPolygon Polygon, IReadOnlyList<GeoPoint> Samples);

public sealed record BoundingBoxScenario(BoundingBox Domain, BoundingBox Query, IReadOnlyList<GeoPoint> Samples);
