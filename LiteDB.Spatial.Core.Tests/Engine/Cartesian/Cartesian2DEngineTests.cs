#nullable enable

extern alias LiteDbBase;

using System;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine.Cartesian;

public sealed class Cartesian2DEngineTests
{
    [Fact]
    public void PlanNear_ShouldProduceAxisAlignedCover()
    {
        var engine = new Cartesian2DEngine("position", new SpatialIndexOptions(precisionBits: 8));
        var center = new GeoPoint(0.5, 0.5);
        var radius = 0.2;
        var plan = engine.PlanNear(center, radius);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        var expectedOffset = radius + engine.Options.DistanceTolerance;
        bounds.MinX.Should().BeApproximately(center.Longitude - expectedOffset, 1e-9);
        bounds.MaxY.Should().BeApproximately(center.Latitude + expectedOffset, 1e-9);
    }

    [Fact]
    public void PlanWithin_ShouldRespectBoundingBox()
    {
        var engine = new Cartesian2DEngine("position", new SpatialIndexOptions(precisionBits: 8));
        var bounds = BoundingBox.From2D(0.1, 0.2, 0.9, 0.95);
        var plan = engine.PlanWithin(bounds);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.Should().Be(bounds);
    }

    [Fact]
    public void Mapper_ShouldReadDocumentWithXY()
    {
        var engine = new Cartesian2DEngine("position");
        var mapper = (Cartesian2DMapper)engine.Mapper;
        var document = new BaseLiteDB.BsonDocument
        {
            ["_id"] = 7,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 0.1,
                ["y"] = 0.9
            }
        };

        GeoPoint point;
        mapper.TryReadPoint(document, out point).Should().BeTrue();
        point.Should().Be(new GeoPoint(0.1, 0.9));
    }
}
