extern alias LiteDbBase;

using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine.Geographic;

public class GeographicEngineTests
{
    [Fact]
    public void PlanNear_ShouldProduceCoveringForCityScaleQuery()
    {
        var options = new SpatialIndexOptions(precisionBits: 20, maxCoveringCells: 128);
        var engine = new GeographicEngine("location", options);
        var center = new GeoPoint(13.4050, 52.5200);

        var plan = engine.PlanNear(center, 5_000);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("5000");
        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MinX.Should().BeLessThan(center.Longitude);
        plan.CoveringBounds.Value.MaxX.Should().BeGreaterThan(center.Longitude);
        plan.CoveringBounds.Value.MinY.Should().BeLessThan(center.Latitude);
        plan.CoveringBounds.Value.MaxY.Should().BeGreaterThan(center.Latitude);
    }

    [Fact]
    public void PlanNear_ShouldHandleAntiMeridianWrap()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 18));
        var center = new GeoPoint(179.5, 0);

        var plan = engine.PlanNear(center, 300_000);

        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MinX.Should().BeApproximately(-180d, 1e-6);
        plan.CoveringBounds.Value.MaxX.Should().BeApproximately(180d, 1e-6);
        plan.IndexRanges.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PlanNear_ShouldExpandToPolesWhenCircleTouchesPole()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions());
        var center = new GeoPoint(40, 89.5);

        var plan = engine.PlanNear(center, 120_000);

        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MinX.Should().Be(-180d);
        plan.CoveringBounds.Value.MaxX.Should().Be(180d);
        plan.CoveringBounds.Value.MaxY.Should().BeLessOrEqualTo(90d);
    }

    [Fact]
    public void Mapper_ShouldReadGeoJsonPoint()
    {
        var engine = new GeographicEngine("location");
        var mapper = engine.Mapper;

        var document = new BaseLiteDB.BsonDocument
        {
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["type"] = "Point",
                ["coordinates"] = new BaseLiteDB.BsonArray { 10.123, 20.456 }
            }
        };

        mapper.TryReadPoint(document, out GeoPoint point).Should().BeTrue();
        point.Longitude.Should().BeApproximately(10.123, 1e-9);
        point.Latitude.Should().BeApproximately(20.456, 1e-9);
    }

    [Fact]
    public void EnsurePointIndex_ShouldPersistMetadata()
    {
        using var stream = new MemoryStream();
        using var database = new BaseLiteDB.LiteDatabase(stream);

        var descriptor = SpatialGeographic.EnsurePointIndex(database, "places", "location", new SpatialIndexOptions(precisionBits: 18));

        descriptor.Engine.Should().NotBeNull();
        descriptor.EngineName.Should().Be(GeographicEngine.EngineName);
        descriptor.Dimensions.Should().Be(2);

        var store = new SpatialMetadataStore(database);
        var loaded = store.GetRequiredDescriptor("places");
        loaded.Should().Be(descriptor);
    }

    [Fact]
    public void Distance_ShouldApproximateKnownCities()
    {
        var distance = new GeographicDistance();
        var berlin = new GeoPoint(13.4050, 52.5200);
        var paris = new GeoPoint(2.3522, 48.8566);

        var result = distance.Distance(berlin, paris);

        result.Should().BeApproximately(878_000, 5_000); // ~878 km between Berlin and Paris
    }
}
