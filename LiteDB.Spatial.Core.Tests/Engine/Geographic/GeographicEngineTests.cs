#nullable enable

extern alias LiteDbBase;

using System;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine.Geographic;

public sealed class GeographicEngineTests
{
    [Fact]
    public void PlanNear_ShouldProduceCoveringForUrbanCoordinates()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 12));
        var center = new GeoPoint(13.4050, 52.5200); // Berlin
        var plan = engine.PlanNear(center, 5_000); // five kilometers

        plan.Dimensions.Should().Be(2);
        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        bounds.MinY.Should().BeLessThan(center.Latitude);
        bounds.MaxY.Should().BeGreaterThan(center.Latitude);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("distance");
    }

    [Fact]
    public void PlanNear_ShouldHandleAntiMeridianWrapAround()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 10));
        var center = new GeoPoint(179.5, 0);
        var plan = engine.PlanNear(center, 150_000);

        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeLessThanOrEqualTo(-180d);
        bounds.MaxX.Should().BeGreaterThanOrEqualTo(180d);
    }

    [Fact]
    public void PlanNear_ShouldExpandToFullLongitudeNearPoles()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 10));
        var center = new GeoPoint(-45d, 89.0);
        var plan = engine.PlanNear(center, 250_000);

        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeApproximately(-180d, 1e-6);
        bounds.MaxX.Should().BeApproximately(180d, 1e-6);
    }

    [Fact]
    public void PlanWithin_ShouldRespectWrapAroundBoundingBoxes()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 10));
        var bounds = BoundingBox.From2D(170d, -10d, 190d, 10d);
        var plan = engine.PlanWithin(bounds);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        var covering = plan.CoveringBounds!.Value;
        covering.MaxX.Should().BeGreaterThan(covering.MinX);
    }

    [Fact]
    public void Mapper_ShouldReadCommonGeoJsonRepresentations()
    {
        var engine = new GeographicEngine("location");
        var mapper = (GeographicMapper)engine.Mapper;
        var point = new GeoPoint(12.3, -45.6);
        var document = new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["type"] = "Point",
                ["coordinates"] = new BaseLiteDB.BsonArray { point.Longitude, point.Latitude }
            }
        };

        GeoPoint extracted;
        mapper.TryReadPoint(document, out extracted).Should().BeTrue();
        extracted.Should().Be(point);
        mapper.Encode(point).Should().BeGreaterThan(0);
    }
}
