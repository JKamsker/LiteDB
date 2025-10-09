extern alias LiteDbBase;

using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Facades;

public sealed class SpatialFacadeTests
{
    [Fact]
    public void GeographicFacadePersistsMetadataAndPlansNearQueries()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());

        var descriptor = LiteDB.Spatial.SpatialGeographic.EnsurePointIndex(database, "places", "location");

        descriptor.EngineName.Should().Contain("Geographic");

        var plan = LiteDB.Spatial.SpatialGeographic.Near(database, "places", new LiteDB.Spatial.GeoPoint(0, 0), 500);
        plan.Dimensions.Should().Be(2);
    }

    [Fact]
    public void Cartesian2DFacadeConfiguresCollection()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());

        var descriptor = LiteDB.Spatial.SpatialCartesian2D.EnsurePointIndex(database, "points", "position");
        descriptor.EngineName.Should().Be(LiteDB.Spatial.Cartesian2DEngine.EngineName);

        var plan = LiteDB.Spatial.SpatialCartesian2D.WithinBoundingBox(database, "points", LiteDB.Spatial.BoundingBox.From2D(-1, -1, 1, 1));
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void Cartesian3DFacadeSupportsNearQueries()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());

        LiteDB.Spatial.SpatialCartesian3D.EnsurePointIndex(database, "cloud", "position");

        var plan = LiteDB.Spatial.SpatialCartesian3D.Near(database, "cloud", new LiteDB.Spatial.GeoPoint3D(0, 0, 0), 5);
        plan.Dimensions.Should().Be(3);
    }
}

