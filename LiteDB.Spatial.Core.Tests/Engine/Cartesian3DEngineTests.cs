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
    public void PlanNearProducesExpectedBounds()
    {
        var engine = new Cartesian3DEngine("position", new SpatialIndexOptions(precisionBits: 8, distanceTolerance: 0.0));
        var center = new GeoPoint3D(0.3d, 0.4d, 0.5d);
        var plan = engine.PlanNear(center, 0.1d);

        plan.Dimensions.Should().Be(3);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();

        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeApproximately(0.2d, 1e-6);
        bounds.MaxX.Should().BeApproximately(0.4d, 1e-6);
        bounds.MinY.Should().BeApproximately(0.3d, 1e-6);
        bounds.MaxY.Should().BeApproximately(0.5d, 1e-6);
        bounds.MinZ.Should().BeApproximately(0.4d, 1e-6);
        bounds.MaxZ.Should().BeApproximately(0.6d, 1e-6);
    }

    [Fact]
    public void PlanNearTwoDimensionalThrows()
    {
        var engine = new Cartesian3DEngine("position");
        var center2D = new GeoPoint(0.1d, 0.2d);

        var action = () => engine.PlanNear(center2D, 0.1d);
        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void EnsurePointIndexBackfillsDocuments()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("cloud");
        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 0.1d,
                ["y"] = 0.2d,
                ["z"] = 0.3d
            }
        });

        var descriptor = SpatialCartesian3D.EnsurePointIndex(database, "cloud", "position", new SpatialIndexOptions(precisionBits: 9));

        descriptor.EngineName.Should().Be(Cartesian3DEngine.EngineName);
        var stored = collection.FindById(1);
        ((object?)stored).Should().NotBeNull();
        stored![descriptor.Options.IndexFieldName].IsNull.Should().BeFalse();
        stored[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(6);
    }

    [Fact]
    public void PlanWithinRequiresThreeDimensionalBox()
    {
        var engine = new Cartesian3DEngine("position");
        var twoDimensional = BoundingBox.From2D(0d, 0d, 1d, 1d);

        var action = () => engine.PlanWithin(twoDimensional);
        action.Should().Throw<ArgumentException>();
    }
}
