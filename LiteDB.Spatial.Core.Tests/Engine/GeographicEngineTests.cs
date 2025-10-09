extern alias LiteDbBase;

using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class GeographicEngineTests
{
    [Fact]
    public void PlanNearProducesCoveringBounds()
    {
        var engine = new GeographicEngine();
        var center = new GeoPoint(13.4050, 52.5200); // Berlin

        var plan = engine.PlanNear(center, 1_000);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("distance");

        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;

        bounds.MinX.Should().BeLessThan(bounds.MaxX);
        bounds.MinY.Should().BeLessThan(bounds.MaxY);
        bounds.MinY.Should().BeLessOrEqualTo(center.Latitude);
        bounds.MaxY.Should().BeGreaterOrEqualTo(center.Latitude);
    }

    [Fact]
    public void PlanWithinSpanningAntiMeridianExpandsCovering()
    {
        var engine = new GeographicEngine();
        var plan = engine.PlanWithin(BoundingBox.From2D(170d, -10d, 190d, 10d));

        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();

        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeApproximately(-180d, 1e-3);
        bounds.MaxX.Should().BeApproximately(180d, 1e-3);
    }

    [Fact]
    public void PlanNearAtHighLatitudeCoversAllLongitudes()
    {
        var engine = new GeographicEngine();
        var plan = engine.PlanNear(new GeoPoint(0d, 89.0), 300_000);

        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().Be(-180d);
        bounds.MaxX.Should().Be(180d);
    }

    [Fact]
    public void DistanceMatchesKnownCities()
    {
        var engine = new GeographicEngine();
        var berlin = new GeoPoint(13.4050, 52.5200);
        var paris = new GeoPoint(2.3522, 48.8566);

        var distance = engine.Distance.Distance(berlin, paris);

        distance.Should().BeApproximately(878_446d, 1_000d);
    }

    [Fact]
    public void MapperReadsGeoJsonPoint()
    {
        var engine = new GeographicEngine();
        var mapper = engine.Mapper;
        var document = new BaseLiteDB.BsonDocument
        {
            [SpatialCollectionDescriptor.DefaultGeometryFieldName] = new BaseLiteDB.BsonDocument
            {
                ["type"] = "Point",
                ["coordinates"] = new BaseLiteDB.BsonArray { 13.4050, 52.5200 }
            }
        };

        mapper.TryReadPoint(document, out GeoPoint point).Should().BeTrue();
        point.Longitude.Should().BeApproximately(13.4050, 1e-6);
        point.Latitude.Should().BeApproximately(52.5200, 1e-6);

        var encoded = mapper.Encode(point);
        encoded.Should().NotBe(0UL);
    }
}
