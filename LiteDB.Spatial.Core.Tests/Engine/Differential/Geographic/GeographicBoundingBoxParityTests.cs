extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential.Geographic;

[Category("oracle")]
public sealed class GeographicBoundingBoxParityTests
{
    [Fact]
    public void WithinBoundingBoxMatchesNtsOracle()
    {
        var scenarios = FixtureLoader.Load<List<BoundingBoxScenario>>("geographic/bounding_box_cases.json");
        var mismatches = new List<string>();

        foreach (var scenario in scenarios)
        {
            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var collection = database.GetCollection<GeoFeature>("points");

            Spatial.UseGeographic(collection, x => x.Location);
            collection.DeleteAll();

            var documents = scenario.Points.Select(point => new GeoFeature(point.Id, new GeoPoint(point.Longitude, point.Latitude))).ToList();
            collection.Insert(documents);

            var descriptor = Spatial.EnsurePointIndex(collection);
            collection.Count().Should().Be(scenario.Points.Count, $"Fixture {scenario.Id} should insert all test points");
            var rawCollection = database.GetCollection("points");
            var rawDocs = rawCollection.FindAll().ToList();
            var bounds = BoundingBox.From2D(scenario.Bounds[0], scenario.Bounds[1], scenario.Bounds[2], scenario.Bounds[3]);

            var plan = SpatialGeographic.WithinBoundingBox(descriptor, bounds);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            AssertIndexExplain(explain);
            var explainText = explain.ToString();

            var actual = Spatial.WithinBoundingBox(collection, x => x.Location, bounds)
                .Select(feature => feature.Id)
                .ToList();

            var expected = NtsOracle.PointsWithinBoundingBox(bounds, scenario.Points)
                .ToList();

            var missing = expected.Except(actual).ToList();
            var unexpected = actual.Except(expected).ToList();

            if (missing.Count > 0 || unexpected.Count > 0)
            {
                var parts = new List<string>();
                if (missing.Count > 0)
                {
                    parts.Add($"missing [{string.Join(", ", missing)}]");
                }

                if (unexpected.Count > 0)
                {
                    parts.Add($"unexpected [{string.Join(", ", unexpected)}]");
                }

                var rawInfo = string.Join("; ", rawDocs.Select(doc =>
                {
                    var id = doc["_id"].AsString;
                    var idx = doc[descriptor.Options.IndexFieldName];
                    var bbox = doc[descriptor.Options.BoundingBoxFieldName];
                    return $"{id}: idx={idx}, bbox={bbox}";
                }));

                mismatches.Add($"{scenario.Id}: {string.Join(", ", parts)} | explain: {explainText.Replace('\n', ' ')} | raw: {rawInfo}");
            }
        }

        mismatches.Should().BeEmpty("LiteDB bounding-box results should mirror the NTS oracle");
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

    private sealed record GeoFeature(string Id, GeoPoint Location);

    private sealed class BoundingBoxScenario
    {
        public string Id { get; set; } = string.Empty;

        public string? Mode { get; set; }

        public double[] Bounds { get; set; } = new double[4];

        public double? ClampMaxLatitude { get; set; }

        public List<NtsOracle.NaturalEarthPoint> Points { get; set; } = new();
    }
}
