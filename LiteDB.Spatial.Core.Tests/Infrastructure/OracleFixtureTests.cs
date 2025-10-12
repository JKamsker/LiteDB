using System;
using FluentAssertions;
using LiteDB.Spatial.Testing.Oracles;
using LiteDB.Spatial.Testing.Oracles.Oracles;
using Xunit;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

public class OracleFixtureTests
{
    [OracleSkip("geographiclib")]
    public void GeographicLib_matches_geodesic_pairs()
    {
        var oracle = new GeographicLibOracle();
        oracle.IsAvailable.Should().BeTrue("the oracle should be enabled by OracleSkip");

        var fixtures = FixtureLoader.LoadGeodesicPairs();
        fixtures.Should().NotBeEmpty();

        foreach (var fixture in fixtures)
        {
            var actual = oracle.GetDistanceMeters(fixture.Start, fixture.End);
            SpatialTolerance.AssertWithin(fixture.ExpectedMeters, actual, SpatialTolerance.EarthDistanceMeters, fixture.Id, "meters");
        }
    }

    [OracleSkip("nts")]
    public void Nts_polygon_metrics_match_fixture_expectations()
    {
        var oracle = new NtsOracle();
        oracle.IsAvailable.Should().BeTrue("the oracle should be enabled by OracleSkip");

        var fixtures = FixtureLoader.LoadPolygons();
        fixtures.Should().NotBeEmpty();

        foreach (var fixture in fixtures)
        {
            var area = oracle.GetAreaSquareMeters(fixture.Rings);
            var perimeter = oracle.GetPerimeterMeters(fixture.Rings);

            if (fixture.ExpectedArea.HasValue)
            {
                SpatialTolerance.AssertWithin(fixture.ExpectedArea.Value, area, SpatialTolerance.CartesianArea, fixture.Id, "m²");
            }

            if (fixture.ExpectedPerimeter.HasValue)
            {
                SpatialTolerance.AssertWithin(fixture.ExpectedPerimeter.Value, perimeter, SpatialTolerance.CartesianDistance, fixture.Id, "m");
            }

            area.Should().BeGreaterThan(0, $"fixture {fixture.Id} should have positive area");
            perimeter.Should().BeGreaterThan(0, $"fixture {fixture.Id} should have positive perimeter");
        }
    }

    [OracleSkip("mathnet")]
    public void MathNet_point_cloud_endpoints_match_hand_calculation()
    {
        var oracle = new MathNetOracle3D();
        oracle.IsAvailable.Should().BeTrue("the oracle should be enabled by OracleSkip");

        var fixtures = FixtureLoader.LoadPointClouds();
        fixtures.Should().NotBeEmpty();

        foreach (var fixture in fixtures)
        {
            var first = fixture.Points[0];
            var last = fixture.Points[^1];
            var actual = oracle.GetDistance(first, last);
            var expected = ComputeDistance(first.X, first.Y, first.Z, last.X, last.Y, last.Z);

            try
            {
                SpatialTolerance.AssertWithin(expected, actual, SpatialTolerance.CartesianDistance, fixture.Id, "units");
            }
            catch (XunitException ex)
            {
                var snapshot = SnapshotOnFailure.WriteJson(fixture.Id, new
                {
                    fixture.Id,
                    first,
                    last,
                    expected,
                    actual,
                    delta = Math.Abs(expected - actual),
                });

                throw new XunitException(ex.Message + $" Snapshot: {snapshot}");
            }
        }
    }

    private static double ComputeDistance(double x1, double y1, double z1, double x2, double y2, double z2)
        => Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));
}
