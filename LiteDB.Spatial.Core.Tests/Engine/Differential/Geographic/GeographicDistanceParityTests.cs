extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential.Geographic;

[Category("oracle")]
public sealed class GeographicDistanceParityTests
{
    [Fact]
    public void HaversineMatchesGeographicLibWithinTolerance()
    {
        ValidatePairs(GeographicDistanceMode.Haversine);
    }

    [Fact]
    public void VincentyMatchesGeographicLibWithinTolerance()
    {
        ValidatePairs(GeographicDistanceMode.Vincenty);
    }

    private static void ValidatePairs(GeographicDistanceMode mode)
    {
        var engine = new GeographicDistance(mode);
        var failures = new List<string>();

        foreach (var pair in GeographicLibOracle.Pairs)
        {
            var left = new GeoPoint(pair.Longitude1, pair.Latitude1);
            var right = new GeoPoint(pair.Longitude2, pair.Latitude2);

            var expected = GeographicLibOracle.Distance(pair);
            var actual = engine.Distance(left, right);
            var tolerance = GetTolerance(mode, expected);

            if (Math.Abs(actual - expected) > tolerance)
            {
                failures.Add($"{pair.Id} (expected={expected:0.###}, actual={actual:0.###})");
            }
        }

        failures.Should().BeEmpty("LiteDB distances should align with GeographicLib within the documented tolerance");

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("geo");
        var metadata = new SpatialMetadataStore(database);
        var descriptor = SpatialGeographic.EnsurePointIndex(metadata, collection, "location", new SpatialIndexOptions(), mode);

        var sample = GeographicLibOracle.Pairs[0];
        var plan = SpatialGeographic.Near(descriptor, new GeoPoint(sample.Longitude1, sample.Latitude1), 1000);
        var explain = SpatialDiagnostics.Explain(plan, descriptor);

        AssertIndexExplain(explain);
    }

    private static double GetTolerance(GeographicDistanceMode mode, double expected)
    {
        return mode == GeographicDistanceMode.Haversine
            ? 5e-3 * expected + 0.05
            : 1e-4 * expected + 0.05;
    }

    private static void AssertIndexExplain(SpatialExplainResult explain)
    {
        explain.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName);
        var explainText = explain.ToString();
        var idxPosition = explainText.IndexOf(SpatialIndexOptions.DefaultIndexFieldName, StringComparison.Ordinal);
        idxPosition.Should().BeGreaterOrEqualTo(0);
        var predicateIndex = explainText.IndexOf("Exact predicate", StringComparison.Ordinal);
        predicateIndex.Should().BeGreaterThan(idxPosition);
    }
}
