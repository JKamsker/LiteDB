extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential.Geographic;

[Category("oracle")]
public sealed class GeographicNearPostgisParityTests
{
    [PostgisFact]
    public async Task LiteDbNearMatchesPostgisWhenEnabled()
    {
        if (!PostgisOracle.TryCreate(out var oracle, out var skipReason))
        {
            throw new InvalidOperationException(skipReason ?? "PostGIS oracle could not be created.");
        }

        var oracleInstance = oracle ?? throw new InvalidOperationException("PostGIS oracle unexpectedly null.");

        await using (oracleInstance)
        {
            var center = new GeoPoint(-149.9, 61.2181); // Anchorage, Alaska
            var radius = 1_000_000d;
            var candidates = BuildCandidates();

            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var collection = database.GetCollection<GeoFeature>("cities");

            Spatial.UseGeographic(collection, x => x.Location);
            collection.DeleteAll();
            collection.Insert(candidates.Select(candidate => new GeoFeature(candidate.Id, new GeoPoint(candidate.Longitude, candidate.Latitude))));

            var descriptor = Spatial.EnsurePointIndex(collection);

            var plan = SpatialGeographic.Near(descriptor, center, radius);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            AssertIndexExplain(explain);

            var liteDbMatches = Spatial.Near(collection, x => x.Location, center, radius)
                .Select(city => city.Id)
                .ToList();

            var postgisMatches = await oracleInstance.QueryDWithinAsync(candidates, center, radius);

            liteDbMatches.Should().BeEquivalentTo(postgisMatches, options => options.WithoutStrictOrdering());
        }
    }

    private static void AssertIndexExplain(SpatialExplainResult explain)
    {
        explain.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName);
        var explainText = explain.ToString();
        var idxPosition = explainText.IndexOf(SpatialIndexOptions.DefaultIndexFieldName, System.StringComparison.Ordinal);
        idxPosition.Should().BeGreaterOrEqualTo(0);
        var predicateIndex = explainText.IndexOf("Exact predicate", System.StringComparison.Ordinal);
        predicateIndex.Should().BeGreaterThan(idxPosition);
    }

    private static IReadOnlyList<PostgisOracle.PostgisPoint> BuildCandidates()
    {
        return new List<PostgisOracle.PostgisPoint>
        {
            new() { Id = "anchorage", Longitude = -149.9, Latitude = 61.2181 },
            new() { Id = "fairbanks", Longitude = -147.7164, Latitude = 64.8378 },
            new() { Id = "seattle", Longitude = -122.3321, Latitude = 47.6062 },
            new() { Id = "juneau", Longitude = -134.4197, Latitude = 58.3019 },
            new() { Id = "nome", Longitude = -165.4064, Latitude = 64.5011 },
            new() { Id = "tokyo", Longitude = 139.6917, Latitude = 35.6895 }
        };
    }

    private sealed record GeoFeature(string Id, GeoPoint Location);
}
