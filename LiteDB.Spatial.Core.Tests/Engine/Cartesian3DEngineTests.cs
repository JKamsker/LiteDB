extern alias LiteDbBase;

using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class Cartesian3DEngineTests
{
    [Fact]
    public void PlanNearProducesExpectedBounds()
    {
        var space = BoundingBox.From3D(0d, 0d, 0d, 10d, 10d, 10d);
        var engine = new Cartesian3DEngine(coordinateSpace: space);

        var plan = engine.PlanNear(new GeoPoint3D(5d, 5d, 5d), 2d);

        plan.Dimensions.Should().Be(3);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();

        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeApproximately(3d, 0.001d);
        bounds.MaxX.Should().BeApproximately(7d, 0.001d);
        bounds.MinY.Should().BeApproximately(3d, 0.001d);
        bounds.MaxY.Should().BeApproximately(7d, 0.001d);
        bounds.MinZ.Should().BeApproximately(3d, 0.001d);
        bounds.MaxZ.Should().BeApproximately(7d, 0.001d);
    }

    [Fact]
    public void PlanWithinOutsideSpaceReturnsEmptyPlan()
    {
        var space = BoundingBox.From3D(0d, 0d, 0d, 10d, 10d, 10d);
        var engine = new Cartesian3DEngine(coordinateSpace: space);

        var plan = engine.PlanWithin(BoundingBox.From3D(20d, 20d, 20d, 30d, 30d, 30d));

        plan.CoveringBounds.Should().BeNull();
        plan.IndexRanges.Should().BeEmpty();
    }

    [Fact]
    public void DistanceUsesEuclidean3D()
    {
        var engine = new Cartesian3DEngine();
        var left = new GeoPoint3D(0d, 0d, 0d);
        var right = new GeoPoint3D(1d, 2d, 2d);

        engine.Distance.Distance(left, right).Should().Be(3d);
    }

    [Fact]
    public void MapperReadsArrayCoordinates()
    {
        var engine = new Cartesian3DEngine();
        var mapper = engine.Mapper;
        var document = new BaseLiteDB.BsonDocument
        {
            [SpatialCollectionDescriptor.DefaultGeometryFieldName] = new BaseLiteDB.BsonArray { 1d, 2d, 3d }
        };

        mapper.TryReadPoint(document, out GeoPoint3D point).Should().BeTrue();
        point.X.Should().Be(1d);
        point.Y.Should().Be(2d);
        point.Z.Should().Be(3d);

        mapper.Invoking(m => m.Encode(point)).Should().NotThrow();
    }
}
