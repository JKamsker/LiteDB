using System;
using FluentAssertions;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine.Cartesian;

public sealed class CartesianEngineTests
{
    [Fact]
    public void Cartesian2DPlanNearProducesRanges()
    {
        var engine = new LiteDB.Spatial.Cartesian2DEngine("position");
        var plan = engine.PlanNear(new LiteDB.Spatial.GeoPoint(10, 15), 5);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void Cartesian3DPlanNearProducesRanges()
    {
        var engine = new LiteDB.Spatial.Cartesian3DEngine("position");
        var plan = engine.PlanNear(new LiteDB.Spatial.GeoPoint3D(1, 2, 3), 2);

        plan.Dimensions.Should().Be(3);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void CartesianMapperRejectsOutOfExtentValues()
    {
        var engine = new LiteDB.Spatial.Cartesian2DEngine("position", xExtent: new LiteDB.Spatial.AxisExtent(-1, 1), yExtent: new LiteDB.Spatial.AxisExtent(-1, 1));

        var action = () => engine.Mapper.Encode(new LiteDB.Spatial.GeoPoint(2, 0));
        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*falls outside the configured extent*");
    }
}

