extern alias LiteDbBase;

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

    private static LiteDbBase::LiteDB.Spatial.GeoPolygon BuildLiteDbPolygon(IReadOnlyList<CartesianPoint2D> ring)
    {
        var outer = ring.Take(ring.Count - 1).Select(p => new LiteDbBase::LiteDB.Spatial.GeoPoint(p.Y, p.X)).ToList();
        outer.Add(new LiteDbBase::LiteDB.Spatial.GeoPoint(ring[0].Y, ring[0].X));
        return new LiteDbBase::LiteDB.Spatial.GeoPolygon(outer);
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
