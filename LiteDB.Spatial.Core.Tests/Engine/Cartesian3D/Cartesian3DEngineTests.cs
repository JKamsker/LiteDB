extern alias LiteDbBase;

using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Engine.Cartesian3D;

public class Cartesian3DEngineTests
{
    [Fact]
    public void PlanNear_ShouldProduceCubeAroundCenter()
    {
        var engine = new Cartesian3DEngine("position", new SpatialIndexOptions(distanceTolerance: 0.1));
        var center = new GeoPoint3D(0.3, 0.4, 0.5);

        var plan = engine.PlanNear(center, 0.05);

        plan.Dimensions.Should().Be(3);
        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MinX.Should().BeApproximately(0.3 - 0.055, 1e-6);
        plan.CoveringBounds.Value.MaxZ.Should().BeApproximately(0.5 + 0.055, 1e-6);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void PlanWithin_ShouldRespect3DBounds()
    {
        var engine = new Cartesian3DEngine("position");
        var bounds = BoundingBox.From3D(0.1, 0.2, 0.3, 0.6, 0.7, 0.8);

        var plan = engine.PlanWithin(bounds);

        plan.CoveringBounds.Should().Be(bounds);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void Mapper_ShouldReadThreeDimensionalArray()
    {
        var engine = new Cartesian3DEngine("position");
        var mapper = engine.Mapper;

        var document = new BaseLiteDB.BsonDocument
        {
            ["position"] = new BaseLiteDB.BsonArray { 0.1, 0.2, 0.3 }
        };

        mapper.TryReadPoint(document, out GeoPoint3D point).Should().BeTrue();
        point.X.Should().BeApproximately(0.1, 1e-9);
        point.Y.Should().BeApproximately(0.2, 1e-9);
        point.Z.Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void EnsurePointIndex_ShouldPersist3DMetadata()
    {
        using var stream = new MemoryStream();
        using var database = new BaseLiteDB.LiteDatabase(stream);

        var descriptor = SpatialCartesian3D.EnsurePointIndex(database, "points3d", "position");

        descriptor.EngineName.Should().Be(Cartesian3DEngine.EngineName);
        descriptor.Dimensions.Should().Be(3);

        var store = new SpatialMetadataStore(database);
        store.GetRequiredDescriptor("points3d").Should().Be(descriptor);
    }
}
