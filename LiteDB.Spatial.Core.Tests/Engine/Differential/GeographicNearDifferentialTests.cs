extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using LiteDB.Spatial;
using Xunit;
using Xunit.Sdk;
using BaseLiteDB = LiteDbBase::LiteDB;
using SpatialFacade = LiteDB.Spatial.Spatial;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

[Category("geographic")]
[Category("oracle")]
public sealed class GeographicNearDifferentialTests
{
    [Fact]
    public void NearExplainUsesIndexAndMatchesVincenty()
    {
        var fixtures = TestDataLoader.LoadCollection<NearQueryFixtureDto>("geographic_near_queries.json")
            .Select(NearQueryFixture.FromDto)
            .ToArray();

        foreach (var fixture in fixtures)
        {
            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var places = database.GetCollection<TestPlace>("places");
            places.DeleteAll();

            var records = fixture.Points
                .Select(point => new TestPlace
                {
                    Id = point.Id,
                    Location = point.Location
                })
                .ToArray();

            places.Insert(records);

            var descriptor = SpatialFacade.UseGeographic(places, x => x.Location, new SpatialIndexOptions(distanceTolerance: 0.5), GeographicDistanceMode.Vincenty);
            SpatialFacade.EnsurePointIndex(places);

            var plan = SpatialGeographic.Near(descriptor, fixture.Center, fixture.RadiusMeters, GeographicDistanceMode.Vincenty);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);

            using var scope = new AssertionScope();
            scope.AddReportable("fixture", fixture.Id);

            explain.IndexFieldName.Should().Be(descriptor.Options.IndexFieldName);
            explain.ExactPredicate.Should().NotBeNullOrWhiteSpace();
            explain.ExactPredicate.Should().Contain("Vincenty");
            plan.IndexRanges.Should().NotBeEmpty();

            var actual = SpatialFacade.Near(places, x => x.Location, fixture.Center, fixture.RadiusMeters)
                .Select(place => place.Id)
                .ToHashSet(StringComparer.Ordinal);

            var distance = new GeographicDistance(GeographicDistanceMode.Vincenty);
            var expected = new HashSet<string>(StringComparer.Ordinal);

            foreach (var point in fixture.Points)
            {
                var meters = distance.Distance(point.Location, fixture.Center);
                if (meters <= fixture.RadiusMeters + descriptor.Options.DistanceTolerance)
                {
                    expected.Add(point.Id);
                }
            }

            actual.Should().BeEquivalentTo(expected);
        }
    }

    [Fact]
    public async Task NearMatchesPostgisWhenEnabled()
    {
        if (!PostgisOracle.TryCreate(out var oracle))
        {
            return;
        }

        await using var postgis = oracle;
        await postgis.InitializeAsync();

        var fixture = NearQueryFixture.FromDto(TestDataLoader.LoadCollection<NearQueryFixtureDto>("geographic_near_queries.json").First());

        await postgis.LoadPointsAsync(fixture.Points);

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var places = database.GetCollection<TestPlace>("places");
        places.DeleteAll();
        var records = fixture.Points.Select(point => new TestPlace { Id = point.Id, Location = point.Location }).ToArray();
        places.Insert(records);

        var descriptor = SpatialFacade.UseGeographic(places, x => x.Location, new SpatialIndexOptions(distanceTolerance: 0.5), GeographicDistanceMode.Vincenty);
        SpatialFacade.EnsurePointIndex(places);

        var plan = SpatialGeographic.Near(descriptor, fixture.Center, fixture.RadiusMeters, GeographicDistanceMode.Vincenty);
        var explain = SpatialDiagnostics.Explain(plan, descriptor);

        explain.IndexFieldName.Should().Be(descriptor.Options.IndexFieldName);
        explain.ExactPredicate.Should().NotBeNullOrWhiteSpace();
        explain.ExactPredicate.Should().Contain("Vincenty");

        var liteDbResults = SpatialFacade.Near(places, x => x.Location, fixture.Center, fixture.RadiusMeters)
            .Select(place => place.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var postgisResults = await postgis.QueryDWithinAsync(fixture.Center, fixture.RadiusMeters);

        liteDbResults.Should().Equal(postgisResults);
    }

    private sealed class TestPlace
    {
        public string Id { get; set; } = string.Empty;

        public GeoPoint Location { get; set; }
    }
}
