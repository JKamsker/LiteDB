#nullable enable

extern alias LiteDbBase;

using System;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine.Cartesian;

public sealed class Cartesian3DEngineTests
{
    [Fact]
    public void PlanNear_ShouldProduceThreeDimensionalCover()
    {
        var engine = new Cartesian3DEngine("position", new SpatialIndexOptions(precisionBits: 6));
        var center = new GeoPoint3D(1.0, 2.0, 3.0);
        var radius = 0.5;
        var plan = engine.PlanNear(center, radius);

        plan.Dimensions.Should().Be(3);
        plan.CoveringBounds.Should().NotBeNull();
        var bounds = plan.CoveringBounds!.Value;
        var expectedOffset = radius + engine.Options.DistanceTolerance;
        bounds.MinZ.Should().BeApproximately(center.Z - expectedOffset, 1e-9);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void PlanWithin_ShouldRequireThreeDimensionalBox()
    {
        var engine = new Cartesian3DEngine("position", new SpatialIndexOptions(precisionBits: 6));
        var bounds = BoundingBox.From3D(0, 0, 0, 1, 2, 3);
        var plan = engine.PlanWithin(bounds);

        plan.Dimensions.Should().Be(3);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        plan.CoveringBounds!.Value.Should().Be(bounds);
    }

    [Fact]
    public void Mapper_ShouldReadDocumentWithXYZ()
    {
        var engine = new Cartesian3DEngine("position");
        var mapper = (Cartesian3DMapper)engine.Mapper;
        var document = new BaseLiteDB.BsonDocument
        {
            ["_id"] = 3,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 1.2,
                ["y"] = -2.3,
                ["z"] = 4.5
            }
        };

        GeoPoint3D point;
        mapper.TryReadPoint(document, out point).Should().BeTrue();
        point.Should().Be(new GeoPoint3D(1.2, -2.3, 4.5));
        mapper.Encode(point).Should().BeGreaterThan(0);
    }
}
