using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests;
using LiteDB.Spatial.Core.Tests.Oracles;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

[Category("property")]
[Category("differential")]
public sealed class CartesianPolygonDifferentialTests
{
    [SpatialProperty(MaxTest = 75, Arbitrary = new[] { typeof(Cartesian2DGenerators) })]
    [Trait("Category", "Oracle")]
    public void ConvexPolygonContainsMatchesOracle(CartesianPolygonCase testCase)
    {
        var snapshot = new
        {
            Domain = testCase.Domain.ToArray(),
            Candidates = testCase.Candidates,
            Polygon = testCase.Polygon
        };

        RunWithSnapshot("polygon-contains", snapshot, () =>
        {
            using var harness = new Cartesian2DTestHarness(testCase.Domain);
            harness.InsertPoints(testCase.Candidates);

            static bool OraclePredicate(CartesianPoint2D point, CartesianPolygonCase @case)
            {
                return NtsOracle.Contains(@case.ToRing(), (point.X, point.Y));
            }

            var polygon = BuildLiteDbPolygon(testCase.Polygon);
            var matches = harness.ExecutePolygon(polygon, p => OraclePredicate(p, testCase), out var breakdown);

            var expected = testCase.Candidates
                .Select((point, index) => (point, id: index + 1))
                .Where(pair => OraclePredicate(pair.point, testCase))
                .Select(pair => pair.id)
                .OrderBy(id => id)
                .ToList();

            matches.Select(m => m.Id).Should().Equal(expected);
            breakdown.Index.Count.Should().BeGreaterOrEqualTo(breakdown.Exact.Count);
            breakdown.Prefilter.Count.Should().BeGreaterOrEqualTo(breakdown.Exact.Count);
        });
    }

    private static GeoPolygon BuildLiteDbPolygon(IReadOnlyList<CartesianPoint2D> ring)
    {
        if (ring == null || ring.Count < 4)
        {
            throw new ArgumentException("Polygons require at least four points (including closure).", nameof(ring));
        }

        var outer = new List<GeoPoint>(ring.Count);
        for (var i = 0; i < ring.Count - 1; i++)
        {
            var point = ring[i];
            outer.Add(new GeoPoint(point.X, point.Y));
        }

        var first = ring[0];
        outer.Add(new GeoPoint(first.X, first.Y));

        return new GeoPolygon(outer);
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
