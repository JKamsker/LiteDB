extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Oracles;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine;

[Category("oracle")]
public sealed class GeographicBoundingBoxOracleTests
{
    [Fact]
    public void WithinBoundingBoxMatchesNtsOracle()
    {
        var fixtures = FixtureLoader.Load<BoundingBoxFixture[]>("fixtures/natural_earth_antimeridian.json");

        foreach (var fixture in fixtures)
        {
            using var scope = new AssertionScope();
            scope.AddReportable("fixture", fixture.Id);

            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var places = database.GetCollection<Place>("places");

            foreach (var point in fixture.Points)
            {
                places.Insert(new Place
                {
                    Id = point.Id,
                    Location = new GeoPoint(point.Lon, point.Lat)
                });
            }

            var descriptor = Spatial.UseGeographic(places, x => x.Location);
            Spatial.EnsurePointIndex(places);

            var bounds = BoundingBox.From2D(fixture.Bounds[0], fixture.Bounds[1], fixture.Bounds[2], fixture.Bounds[3]);
            var plan = SpatialGeographic.WithinBoundingBox(descriptor, bounds);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            var summary = explain.ToString();

            summary.Should().Contain("_idx");
            summary.Should().Contain("Exact predicate");
            summary.IndexOf("_idx", StringComparison.Ordinal).Should()
                .BeLessThan(summary.IndexOf("Exact predicate", StringComparison.Ordinal));

            var expected = NtsOracle.WithinBoundingBox(fixture);
            var actual = Spatial.WithinBoundingBox(places, x => x.Location, bounds)
                .Select(x => x.Id)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            actual.Should().Equal(expected);
        }
    }

    private sealed class Place
    {
        public string Id { get; set; } = string.Empty;

        public GeoPoint Location { get; set; }
    }
}
