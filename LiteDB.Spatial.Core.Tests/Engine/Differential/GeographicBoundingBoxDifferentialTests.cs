extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;
using SpatialFacade = LiteDB.Spatial.Spatial;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

[Category("oracle")]
public sealed class GeographicBoundingBoxDifferentialTests
{
    [Fact]
    public void WithinBoundingBoxMatchesNtsOracle()
    {
        var fixtures = TestDataLoader.LoadCollection<BoundingBoxFixtureDto>("geographic_bounding_boxes.json")
            .Select(BoundingBoxFixture.FromDto)
            .ToArray();

        foreach (var fixture in fixtures)
        {
            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var places = database.GetCollection<TestPlace>("places");
            places.DeleteAll();

            var records = fixture.Points
                .Select((point, index) => new TestPlace
                {
                    Id = point.Id,
                    Location = point.Location
                })
                .ToArray();

            places.Insert(records);

            var descriptor = SpatialFacade.UseGeographic(places, x => x.Location);
            SpatialFacade.EnsurePointIndex(places);

            var plan = SpatialGeographic.WithinBoundingBox(descriptor, fixture.Bounds);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);

            using var scope = new AssertionScope();
            scope.AddReportable("fixture", fixture.Id);

            explain.IndexFieldName.Should().Be(descriptor.Options.IndexFieldName);
            explain.ExactPredicate.Should().NotBeNullOrWhiteSpace();
            explain.ExactPredicate.Should().Contain("Within");
            plan.IndexRanges.Should().NotBeEmpty();

            var actual = SpatialFacade.WithinBoundingBox(places, x => x.Location, fixture.Bounds)
                .Select(place => place.Id)
                .ToHashSet(StringComparer.Ordinal);

            var expected = NtsOracle.WithinBoundingBox(fixture.Bounds, fixture.Points);

            actual.Should().BeEquivalentTo(expected);
        }
    }

    private sealed class TestPlace
    {
        public string Id { get; set; } = string.Empty;

        public GeoPoint Location { get; set; }
    }
}
