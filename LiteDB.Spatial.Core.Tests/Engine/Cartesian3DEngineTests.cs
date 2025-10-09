extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class Cartesian3DEngineTests
{
    [Fact]
    public void PlanNearProducesNormalizedBounds()
    {
        var domain = BoundingBox.From3D(0, 0, 0, 100, 100, 100);
        var engine = new Cartesian3DEngine("position", domain, new SpatialIndexOptions(precisionBits: 9));

        var plan = engine.PlanNear(new GeoPoint3D(10, 10, 10), 2);

        plan.Dimensions.Should().Be(3);
        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.MinZ.Should().BeApproximately(0.08, 1e-6);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void PlanNearRejects2DCenters()
    {
        var domain = BoundingBox.From3D(0, 0, 0, 1, 1, 1);
        var engine = new Cartesian3DEngine("position", domain, new SpatialIndexOptions());

        var action = () => engine.PlanNear(new GeoPoint(0, 0), 1);
        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SpatialCartesian3DEnsurePointIndexAttachesEngine()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("cart3d");
        var metadata = new SpatialMetadataStore(database);
        var domain = BoundingBox.From3D(-10, -10, -10, 10, 10, 10);

        var descriptor = SpatialCartesian3D.EnsurePointIndex(metadata, collection, "position", domain, new SpatialIndexOptions(precisionBits: 6));

        descriptor.Engine.Should().BeOfType<Cartesian3DEngine>();
        SpatialCartesian3D.Near(descriptor, new GeoPoint3D(0, 0, 0), 1).IndexRanges.Should().NotBeEmpty();
    }
}
