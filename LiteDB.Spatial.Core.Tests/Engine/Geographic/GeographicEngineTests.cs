using System;
using FluentAssertions;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine.Geographic;

public sealed class GeographicEngineTests
{
    [Fact]
    public void PlanNearReturnsMortonRanges()
    {
        var engine = new LiteDB.Spatial.GeographicEngine("location");
        var plan = engine.PlanNear(new LiteDB.Spatial.GeoPoint(13.4050, 52.5200), 1000);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("1000");
    }

    [Fact]
    public void PlanWithinHandlesAntiMeridianBoundingBoxes()
    {
        var engine = new LiteDB.Spatial.GeographicEngine("location");
        var bounds = LiteDB.Spatial.BoundingBox.From2D(170, -10, 190, 10);

        var plan = engine.PlanWithin(bounds);

        plan.IndexRanges.Count.Should().BeGreaterThan(0);
        plan.CoveringBounds.Should().BeNull();
    }

    [Fact]
    public void MapperValidatesLatitudeRange()
    {
        var engine = new LiteDB.Spatial.GeographicEngine("location");

        var action = () => engine.Mapper.Encode(new LiteDB.Spatial.GeoPoint(0, 95));
        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Latitude must be within [-90, 90]*");
    }
}

