extern alias LiteDbBase;

using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class Cartesian2DEngineTests
{
    [Fact]
    public void PlanNearProducesExpectedBounds()
    {
        var space = BoundingBox.From2D(0d, 0d, 100d, 100d);
        var engine = new Cartesian2DEngine(coordinateSpace: space);

        var plan = engine.PlanNear(new GeoPoint(50d, 50d), 10d);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();

        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeApproximately(40d, 0.001d);
        bounds.MaxX.Should().BeApproximately(60d, 0.001d);
        bounds.MinY.Should().BeApproximately(40d, 0.001d);
        bounds.MaxY.Should().BeApproximately(60d, 0.001d);
    }

    [Fact]
    public void PlanWithinOutsideSpaceReturnsEmptyPlan()
    {
        var space = BoundingBox.From2D(0d, 0d, 100d, 100d);
        var engine = new Cartesian2DEngine(coordinateSpace: space);

        var plan = engine.PlanWithin(BoundingBox.From2D(200d, 200d, 210d, 210d));

        plan.CoveringBounds.Should().BeNull();
        plan.IndexRanges.Should().BeEmpty();
    }

    [Fact]
    public void DistanceUsesEuclideanMetric()
    {
        var engine = new Cartesian2DEngine();
        var left = new GeoPoint(0d, 0d);
        var right = new GeoPoint(3d, 4d);

        engine.Distance.Distance(left, right).Should().Be(5d);
    }

    [Fact]
    public void MapperReadsDocumentCoordinates()
    {
        var engine = new Cartesian2DEngine(coordinateSpace: BoundingBox.From2D(0d, 0d, 10d, 10d), geometryFieldName: "location");
        var mapper = engine.Mapper;
        var document = new BaseLiteDB.BsonDocument
        {
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 2.5,
                ["y"] = 7.5
            }
        };

        mapper.TryReadPoint(document, out GeoPoint point).Should().BeTrue();
        point.Longitude.Should().Be(2.5);
        point.Latitude.Should().Be(7.5);

        mapper.Invoking(m => m.Encode(point)).Should().NotThrow();
    }
}
