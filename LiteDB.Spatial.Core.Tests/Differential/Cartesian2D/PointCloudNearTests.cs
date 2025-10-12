using System;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

[Category("differential")]
public sealed class PointCloudNearTests
{
    [Fact]
    public void NearQueriesMatchEuclideanExpectations()
    {
        var fixture = PointCloudFixture.Load("point_clouds/unit_grid.json");
        var snapshot = new
        {
            fixture.Id,
            Domain = fixture.Domain.ToArray(),
            Points = fixture.Points,
            Queries = fixture.Queries
        };

        RunWithSnapshot(fixture.Id + "-near", snapshot, () =>
        {
            using var harness = new Cartesian2DTestHarness(fixture.Domain);
            harness.InsertPoints(fixture.Points.Select(p => p.Position));

            foreach (var query in fixture.Queries)
            {
                var matches = harness.ExecuteNear((query.Center.X, query.Center.Y), query.Radius, out var breakdown);
                var expected = fixture.Points
                    .Select((sample, index) => (sample, id: index + 1, distance: Distance(sample.Position, query.Center)))
                    .Where(entry => entry.distance <= query.Radius + NumericTolerance.ForDistance(Math.Max(query.Radius, entry.distance)))
                    .OrderBy(entry => entry.id)
                    .ToList();

                matches.Select(m => m.Id).Should().Equal(expected.Select(e => e.id));
                matches.Select(m => Distance(m.Point, query.Center)).Zip(expected, (actual, exp) => (actual, exp.distance))
                    .ToList()
                    .ForEach(pair => pair.actual.Should().BeApproximately(pair.distance, NumericTolerance.ForDistance(Math.Max(pair.distance, query.Radius))));

                breakdown.Index.Count.Should().BeGreaterOrEqualTo(breakdown.Prefilter.Count);
                breakdown.Prefilter.Count.Should().BeGreaterOrEqualTo(breakdown.Exact.Count);
                breakdown.Exact.Count.Should().Be(expected.Count);
            }
        });
    }

    [Fact]
    public void MortonOrderIsStableAcrossRepeatedQueries()
    {
        var fixture = PointCloudFixture.Load("point_clouds/unit_grid.json");
        var snapshot = new
        {
            fixture.Id,
            Domain = fixture.Domain.ToArray(),
            Points = fixture.Points,
            Queries = fixture.Queries
        };

        RunWithSnapshot(fixture.Id + "-order", snapshot, () =>
        {
            using var harness = new Cartesian2DTestHarness(fixture.Domain);
            harness.InsertPoints(fixture.Points.Select(p => p.Position));

            var baseline = harness.ExecuteNear((fixture.Queries[0].Center.X, fixture.Queries[0].Center.Y), fixture.Queries[0].Radius, out _)
                .Select(p => p.Id)
                .ToList();

            for (var i = 0; i < 5; i++)
            {
                var iteration = harness.ExecuteNear((fixture.Queries[0].Center.X, fixture.Queries[0].Center.Y), fixture.Queries[0].Radius, out _)
                    .Select(p => p.Id)
                    .ToList();

                iteration.Should().Equal(baseline);
            }
        });
    }

    private static double Distance(CartesianPoint2D point, CartesianPoint2D center)
    {
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void RunWithSnapshot<T>(string scenario, T payload, Action body)
    {
        try
        {
            body();
        }
        catch (Exception)
        {
            FailureSnapshot.Capture(scenario, payload!);
            throw;
        }
    }
}
