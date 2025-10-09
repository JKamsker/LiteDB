extern alias LiteDbBase;

#nullable enable

using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class GeographicEngineTests
{
    [Fact]
    public void PlanNearExpandsWhenTouchingPole()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 12));
        var plan = engine.PlanNear(new GeoPoint(0d, 89.9d), 50_000d);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();

        var box = plan.CoveringBounds!.Value;
        box.MaxY.Should().Be(90d);
        box.MinX.Should().BeLessOrEqualTo(-180d);
        box.MaxX.Should().BeGreaterOrEqualTo(180d);
    }

    [Fact]
    public void PlanWithinSplitsAntiMeridian()
    {
        var engine = new GeographicEngine("location", new SpatialIndexOptions(precisionBits: 10));
        var bounds = BoundingBox.From2D(170d, -10d, 190d, 10d);
        var plan = engine.PlanWithin(bounds);

        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MaxX.Should().BeGreaterThan(180d);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void DistanceMatchesKnownCities()
    {
        var distance = new GeographicDistance();
        var london = new GeoPoint(-0.1278d, 51.5074d);
        var paris = new GeoPoint(2.3522d, 48.8566d);

        var result = distance.Distance(london, paris);

        result.Should().BeApproximately(343_941d, 1_000d);
    }

    [Fact]
    public void EnsurePointIndexPersistsMetadataAndBackfills()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("places");
        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["longitude"] = -0.1278d,
                ["latitude"] = 51.5074d
            }
        });

        var descriptor = SpatialGeographic.EnsurePointIndex(database, "places", "location", new SpatialIndexOptions(precisionBits: 12));

        descriptor.EngineName.Should().Be(GeographicEngine.EngineName);
        var storedDocument = collection.FindById(1);
        ((object?)storedDocument).Should().NotBeNull();
        storedDocument![descriptor.Options.IndexFieldName].IsNull.Should().BeFalse();
        storedDocument[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(4);

        var store = new SpatialMetadataStore(database);
        store.TryGetDescriptor("places", out var persisted).Should().BeTrue();
        persisted!.EngineName.Should().Be(GeographicEngine.EngineName);
    }
}
