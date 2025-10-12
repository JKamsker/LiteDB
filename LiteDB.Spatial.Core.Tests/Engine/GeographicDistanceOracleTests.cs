extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using FluentAssertions;
using FluentAssertions.Execution;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine;

[Category("oracle")]
public sealed class GeographicDistanceOracleTests
{
    [Fact]
    public void HaversineMatchesGeographicLibWithinTolerance()
    {
        var pairs = FixtureLoader.Load<GeodesicPair[]>("fixtures/geodesic_pairs.json");
        var distance = new GeographicDistance(GeographicDistanceMode.Haversine);

        foreach (var pair in pairs)
        {
            using var scope = new AssertionScope();
            scope.AddReportable("fixture", pair.Id);

            var left = new GeoPoint(pair.Lon1, pair.Lat1);
            var right = new GeoPoint(pair.Lon2, pair.Lat2);

            var expected = GeographicLibOracle.Distance(pair.Lon1, pair.Lat1, pair.Lon2, pair.Lat2);
            var actual = distance.Distance(left, right);
            var tolerance = expected * 7e-3 + 10; // Haversine assumes a spherical earth; allow ~0.7% error.

            actual.Should().BeApproximately(expected, tolerance);
        }

        AssertExplainUsesIndex(GeographicDistanceMode.Haversine);
    }

    [Fact]
    public void VincentyMatchesGeographicLibWithinTolerance()
    {
        var pairs = FixtureLoader.Load<GeodesicPair[]>("fixtures/geodesic_pairs.json");
        var distance = new GeographicDistance(GeographicDistanceMode.Vincenty);

        foreach (var pair in pairs)
        {
            using var scope = new AssertionScope();
            scope.AddReportable("fixture", pair.Id);

            var left = new GeoPoint(pair.Lon1, pair.Lat1);
            var right = new GeoPoint(pair.Lon2, pair.Lat2);

            var expected = GeographicLibOracle.Distance(pair.Lon1, pair.Lat1, pair.Lon2, pair.Lat2);
            var actual = distance.Distance(left, right);
            var tolerance = expected * 1e-4 + 0.05;

            actual.Should().BeApproximately(expected, tolerance);
        }

        AssertExplainUsesIndex(GeographicDistanceMode.Vincenty);
    }

    private static void AssertExplainUsesIndex(GeographicDistanceMode mode)
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("pairs");
        var metadata = new SpatialMetadataStore(database);
        var descriptor = SpatialGeographic.EnsurePointIndex(metadata, collection, "location", new SpatialIndexOptions(), mode);

        var plan = SpatialGeographic.Near(descriptor, new GeoPoint(0, 0), 1_000, mode);
        var explain = SpatialDiagnostics.Explain(plan, descriptor);
        var summary = explain.ToString();

        summary.Should().Contain("_idx");
        summary.Should().Contain("Exact predicate");
        summary.IndexOf("_idx", StringComparison.Ordinal).Should()
            .BeLessThan(summary.IndexOf("Exact predicate", StringComparison.Ordinal));
    }
}
