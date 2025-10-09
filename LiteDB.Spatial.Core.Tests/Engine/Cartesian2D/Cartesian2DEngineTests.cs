extern alias LiteDbBase;

using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine.Cartesian2D;

public class Cartesian2DEngineTests
{
    [Fact]
    public void PlanNear_ShouldProduceAxisAlignedBoundingBox()
    {
        var engine = new Cartesian2DEngine("position", new SpatialIndexOptions(distanceTolerance: 0.05));
        var center = new GeoPoint(0.25, 0.75);

        var plan = engine.PlanNear(center, 0.1);

        plan.Dimensions.Should().Be(2);
        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MinX.Should().BeApproximately(0.25 - 0.105, 1e-6);
        plan.CoveringBounds.Value.MaxX.Should().BeApproximately(0.25 + 0.105, 1e-6);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void PlanWithin_ShouldRespectInputBounds()
    {
        var engine = new Cartesian2DEngine("position");
        var bounds = BoundingBox.From2D(0.1, 0.2, 0.4, 0.6);

        var plan = engine.PlanWithin(bounds);

        plan.CoveringBounds.Should().Be(bounds);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void Mapper_ShouldReadArrayPoints()
    {
        var engine = new Cartesian2DEngine("position");
        var mapper = engine.Mapper;

        var document = new BaseLiteDB.BsonDocument
        {
            ["position"] = new BaseLiteDB.BsonArray { 0.1, 0.2 }
        };

        mapper.TryReadPoint(document, out GeoPoint point).Should().BeTrue();
        point.Longitude.Should().BeApproximately(0.1, 1e-9);
        point.Latitude.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void EnsurePointIndex_ShouldPersistMetadata()
    {
        using var stream = new MemoryStream();
        using var database = new BaseLiteDB.LiteDatabase(stream);

        var descriptor = SpatialCartesian2D.EnsurePointIndex(database, "points2d", "position");

        descriptor.EngineName.Should().Be(Cartesian2DEngine.EngineName);
        descriptor.Dimensions.Should().Be(2);

        var store = new SpatialMetadataStore(database);
        store.GetRequiredDescriptor("points2d").Should().Be(descriptor);
    }
}
