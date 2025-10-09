#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Differential;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class BoundingBoxDifferentialTests
{
    [Fact]
    [Trait("Category", "Oracle")]
    public void WithinBoundingBoxMatchesNtsOracle()
    {
        var property = Prop.ForAll(BoundingBoxScenarioArbitrary(), scenario =>
        {
            using var harness = new Cartesian2DTestHarness(scenario.Domain);
            var documents = scenario.Points
                .Select(sample => new Cartesian2DTestHarness.CartesianPoint(sample.Id, sample.Position));
            harness.InsertPoints(documents);

            var polygon = NtsOracle.BuildConvexPolygon(scenario.HullPoints);
            var hullBounds = NtsOracle.ToBoundingBox(polygon);

            var hullResults = harness.QueryWithinBounds(hullBounds);
            var hullExpected = NtsOracle.PointsWithinBoundingBox(scenario.Points, hullBounds);
            hullResults.Should().BeEquivalentTo(hullExpected, options => options.WithoutStrictOrdering());

            var axisResults = harness.QueryWithinBounds(scenario.AxisAlignedBounds);
            var axisExpected = NtsOracle.PointsWithinBoundingBox(scenario.Points, scenario.AxisAlignedBounds);
            axisResults.Should().BeEquivalentTo(axisExpected, options => options.WithoutStrictOrdering());

            return true;
        });

        FsCheckPropertyRunner.Check("cartesian2d-bbox-parity", property);
    }

    private static Arbitrary<BoundingBoxScenario> BoundingBoxScenarioArbitrary()
    {
        return Arb.From(BoundingBoxScenarioGenerator());
    }

    private static Gen<BoundingBoxScenario> BoundingBoxScenarioGenerator()
    {
        var domain = BoundingBox.From2D(-128, -128, 128, 128);
        var pointGenerator = PointGenerator(domain);

        return
            from count in Gen.Choose(12, 40)
            from rawPoints in Gen.ArrayOf(pointGenerator, count)
            let samples = BuildSamples(rawPoints)
            where samples.Count >= 8
            let hullCount = Math.Min(samples.Count, 8)
            let hullList = samples.Take(hullCount).ToList()
            let hullPolygon = NtsOracle.BuildConvexPolygon(hullList.Select((PointSample sample) => sample.Position).ToList())
            let hullBounds = NtsOracle.ToBoundingBox(hullPolygon)
            where IsValidBounds(hullBounds)
            from axisBounds in RandomBoundingBoxGenerator(domain)
            select new BoundingBoxScenario(domain, samples, hullList.Select((PointSample sample) => sample.Position).ToList(), axisBounds);
    }

    private static Gen<GeoPoint> PointGenerator(BoundingBox domain)
    {
        return
            from x in ConstrainedDouble(domain.MinX, domain.MaxX)
            from y in ConstrainedDouble(domain.MinY, domain.MaxY)
            select new GeoPoint(x, y);
    }

    private static Gen<double> ConstrainedDouble(double min, double max)
    {
        if (min >= max)
        {
            throw new ArgumentException("Invalid range for constrained double generation.", nameof(min));
        }

        return Gen.Choose(0, 10_000).Select(index =>
        {
            var fraction = index / 10_000d;
            return min + fraction * (max - min);
        });
    }

    private static Gen<BoundingBox> RandomBoundingBoxGenerator(BoundingBox domain)
    {
        return
            from x1 in ConstrainedDouble(domain.MinX, domain.MaxX)
            from x2 in ConstrainedDouble(domain.MinX, domain.MaxX)
            from y1 in ConstrainedDouble(domain.MinY, domain.MaxY)
            from y2 in ConstrainedDouble(domain.MinY, domain.MaxY)
            let minX = Math.Min(x1, x2)
            let maxX = Math.Max(x1, x2)
            let minY = Math.Min(y1, y2)
            let maxY = Math.Max(y1, y2)
            where maxX - minX > 0.25 && maxY - minY > 0.25
            select BoundingBox.From2D(minX, minY, maxX, maxY);
    }

    private static List<PointSample> BuildSamples(IEnumerable<GeoPoint> rawPoints)
    {
        var distinct = rawPoints
            .Select(point => point)
            .DistinctBy(point => (Math.Round(point.Longitude, 6), Math.Round(point.Latitude, 6)))
            .ToList();

        var id = 1;
        var samples = new List<PointSample>(distinct.Count);
        foreach (var point in distinct)
        {
            samples.Add(new PointSample(id++, point));
        }

        return samples;
    }

    private static bool IsValidBounds(BoundingBox bounds)
    {
        return bounds.MaxX - bounds.MinX > 1e-4 && bounds.MaxY - bounds.MinY > 1e-4;
    }

    private sealed record BoundingBoxScenario(
        BoundingBox Domain,
        IReadOnlyList<PointSample> Points,
        IReadOnlyList<GeoPoint> HullPoints,
        BoundingBox AxisAlignedBounds);
}
