extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Testing.Oracles;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Geographic;

[Category("oracle")]
public sealed class GeographicBoundingBoxOracleTests
{
    private const string FixturePath = "fixtures/geographic_bounding_boxes.json";

    [Fact]
    public void WithinBoundingBoxMatchesNtsOracleExpectations()
    {
        var fixtures = LoadFixture();

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<GeographicPoint>("geo_points");
        var descriptor = Spatial.UseGeographic(collection, x => x.Location);

        var documents = fixtures.Points
            .Select(p => new GeographicPoint(p.Id, p.Name, new GeoPoint(p.Lon, p.Lat)))
            .ToList();

        collection.Insert(documents);
        Spatial.EnsurePointIndex(collection);
        collection.Count().Should().Be(fixtures.Points.Count);

        foreach (var box in fixtures.Boxes)
        {
            if (box.Bounds.Length != 4)
            {
                throw new InvalidOperationException($"Fixture {box.Id} must provide four coordinates.");
            }

            var (minLon, minLat, maxLon, maxLat) = (box.Bounds[0], box.Bounds[1], box.Bounds[2], box.Bounds[3]);
            var clampedMinLat = Math.Clamp(minLat, -90d, 90d);
            var clampedMaxLat = Math.Clamp(maxLat, -90d, 90d);
            var bounds = BoundingBox.From2D(minLon, clampedMinLat, maxLon, clampedMaxLat);
            var plan = SpatialGeographic.WithinBoundingBox(descriptor, bounds);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            var summary = explain.ToString();

            var indexPosition = summary.IndexOf("_idx", StringComparison.Ordinal);
            var predicatePosition = summary.IndexOf("Exact predicate", StringComparison.Ordinal);

            indexPosition.Should().BeGreaterThanOrEqualTo(0, $"Explain missing _idx predicate for {box.Id}");
            predicatePosition.Should().BeGreaterThanOrEqualTo(0, $"Explain missing exact predicate for {box.Id}");
            predicatePosition.Should().BeGreaterThan(indexPosition, $"Explain for {box.Id} should list index usage before predicate.");

            var actual = Spatial.WithinBoundingBox(collection, x => x.Location, bounds)
                .Select(point => point.Id)
                .OrderBy(id => id)
                .ToList();

            var expected = fixtures.Points
                .Where(point => NtsOracle.Contains((minLon, clampedMinLat, maxLon, clampedMaxLat), point.Lon, point.Lat))
                .Select(point => point.Id)
                .OrderBy(id => id)
                .ToList();

            actual.Should().Equal(expected, $"Fixture {box.Id} ({box.Description})");
        }
    }

    private static BoundingBoxFixture LoadFixture()
    {
        using var stream = File.OpenRead(FixturePath);
        var document = JsonSerializer.Deserialize<BoundingBoxFixture>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return document ?? throw new InvalidOperationException($"Fixture '{FixturePath}' could not be loaded.");
    }

    private sealed record GeographicPoint(int Id, string Name, GeoPoint Location);

    private sealed class BoundingBoxFixture
    {
        public List<PointFixture> Points { get; init; } = new();
        public List<BoxFixture> Boxes { get; init; } = new();
    }

    private sealed record PointFixture(int Id, string Name, double Lon, double Lat);

    private sealed record BoxFixture(string Id, string Description, double[] Bounds);
}
