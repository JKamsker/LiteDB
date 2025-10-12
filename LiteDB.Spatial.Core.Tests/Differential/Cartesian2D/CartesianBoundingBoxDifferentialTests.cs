using System;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class CartesianBoundingBoxDifferentialTests
{
    [SpatialProperty(MaxTest = 100, Arbitrary = new[] { typeof(Cartesian2DGenerators) })]
    public void AxisAlignedBoundingBoxesMatchManualInequalities(CartesianBoxCase testCase)
    {
        var snapshot = new
        {
            Domain = testCase.Domain.ToArray(),
            Query = testCase.Query.ToArray(),
            Points = testCase.Points
        };

        RunWithSnapshot("aabb-within", snapshot, () =>
        {
            using var harness = new Cartesian2DTestHarness(testCase.Domain);
            harness.InsertPoints(testCase.Points);

            var results = harness.ExecuteBoundingBox(testCase.Query, out var breakdown);
            var expected = testCase.Points
                .Select((point, index) => (point, id: index + 1))
                .Where(pair => pair.point.X >= testCase.Query.MinX && pair.point.X <= testCase.Query.MaxX
                               && pair.point.Y >= testCase.Query.MinY && pair.point.Y <= testCase.Query.MaxY)
                .Select(pair => pair.id)
                .OrderBy(id => id)
                .ToList();

            results.Select(r => r.Id).Should().Equal(expected);
            breakdown.Index.Count.Should().BeGreaterOrEqualTo(breakdown.Prefilter.Count);
            breakdown.Prefilter.Count.Should().BeGreaterOrEqualTo(breakdown.Exact.Count);
        });
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
