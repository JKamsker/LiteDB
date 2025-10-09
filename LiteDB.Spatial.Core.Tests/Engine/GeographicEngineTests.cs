extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class GeographicEngineTests
{
    [Fact]
    public void PlanNearProducesCoveringAndPredicate()
    {
        var options = new SpatialIndexOptions(precisionBits: 10, maxCoveringCells: 32, distanceTolerance: 5);
        var engine = new GeographicEngine("location", options, GeographicDistanceMode.Haversine);

        var center = new GeoPoint(13.405, 52.52);
        var plan = engine.PlanNear(center, 1000);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeGreaterThan(-181);
        bounds.MaxX.Should().BeLessThan(181);
        bounds.MinY.Should().BeGreaterThan(-91);
        bounds.MaxY.Should().BeLessThan(91);
        bounds.MinX.Should().BeLessOrEqualTo(center.Longitude);
        bounds.MaxX.Should().BeGreaterOrEqualTo(center.Longitude);
        bounds.MinY.Should().BeLessOrEqualTo(center.Latitude);
        bounds.MaxY.Should().BeGreaterOrEqualTo(center.Latitude);
        plan.ExactPredicateDescription.Should().Contain("Haversine");
    }

    [Fact]
    public void PlanNearSupportsVincentyMode()
    {
        var options = new SpatialIndexOptions(precisionBits: 12, maxCoveringCells: 64, distanceTolerance: 0.1);
        var engine = new GeographicEngine("location", options, GeographicDistanceMode.Vincenty);

        var plan = engine.PlanNear(new GeoPoint(-74.0, 40.71), 2500);

        plan.ExactPredicateDescription.Should().Contain("Vincenty");
    }

    [Fact]
    public void PlanWithinExpandsAntiMeridianBoundingBoxes()
    {
        var options = new SpatialIndexOptions(precisionBits: 8, maxCoveringCells: 32, distanceTolerance: 1);
        var engine = new GeographicEngine("location", options);

        var bounds = BoundingBox.From2D(170, -10, 190, 10);
        var plan = engine.PlanWithin(bounds);

        plan.CoveringBounds.Should().NotBeNull();
        var covering = plan.CoveringBounds!.Value;
        covering.MinX.Should().Be(-180);
        covering.MaxX.Should().Be(180);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void PlanNearHandlesPolarExpansion()
    {
        var options = new SpatialIndexOptions(precisionBits: 9, maxCoveringCells: 64, distanceTolerance: 10);
        var engine = new GeographicEngine("location", options);

        var plan = engine.PlanNear(new GeoPoint(0, 88), 500000);

        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MaxY.Should().BeApproximately(90, 1e-3);
    }

    [Fact]
    public void PlanNearRejectsInvalidCoordinates()
    {
        var options = new SpatialIndexOptions();
        var engine = new GeographicEngine("location", options);

        Action action = () => engine.PlanNear(new GeoPoint(500, 0), 100);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PlanWithinRejectsOutOfRangeLatitude()
    {
        var options = new SpatialIndexOptions();
        var engine = new GeographicEngine("location", options);

        var bounds = BoundingBox.From2D(-10, -120, 10, 0);
        Action action = () => engine.PlanWithin(bounds);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SpatialGeographicEnsurePointIndexPersistsDescriptor()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("geo");
        var metadata = new SpatialMetadataStore(database);

        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["longitude"] = 2,
                ["latitude"] = 2
            }
        });

        var descriptor = SpatialGeographic.EnsurePointIndex(metadata, collection, "location", new SpatialIndexOptions(precisionBits: 8), GeographicDistanceMode.Haversine);

        descriptor.Engine.Should().NotBeNull();
        descriptor.Engine!.Name.Should().Be(GeographicEngine.EngineName);
        descriptor.Settings.DistanceMode.Should().Be(GeographicDistanceMode.Haversine);

        var plan = SpatialGeographic.Near(descriptor, new GeoPoint(2, 2), 500);
        plan.IndexRanges.Should().NotBeEmpty();

        var stored = collection.FindById(new BaseLiteDB.BsonValue(1));
        ((object?)stored).Should().NotBeNull();
        var bounding = stored![descriptor.Options.BoundingBoxFieldName].AsArray;
        bounding.Count.Should().Be(4);
        bounding[0].AsDouble.Should().BeApproximately(2, 1e-6);
        bounding[1].AsDouble.Should().BeApproximately(2, 1e-6);
    }

    [Fact]
    public void SpatialGeographicNearRequiresGeographicDescriptor()
    {
        var descriptor = new SpatialCollectionDescriptor("points", Cartesian2DEngine.EngineName, 2, "location", new SpatialIndexOptions());

        Action action = () => SpatialGeographic.Near(descriptor, new GeoPoint(0, 0), 10);
        action.Should().Throw<SpatialMetadataException>();
    }
}
