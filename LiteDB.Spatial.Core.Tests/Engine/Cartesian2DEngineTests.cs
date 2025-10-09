extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class Cartesian2DEngineTests
{
    [Fact]
    public void PlanNearProducesExpectedBounds()
    {
        var engine = new Cartesian2DEngine("position", new SpatialIndexOptions(precisionBits: 8, distanceTolerance: 0.0));
        var center = new GeoPoint(0.5d, 0.5d);
        var plan = engine.PlanNear(center, 0.25d);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();

        var bounds = plan.CoveringBounds!.Value;
        bounds.MinX.Should().BeApproximately(0.25d, 1e-6);
        bounds.MaxX.Should().BeApproximately(0.75d, 1e-6);
        bounds.MinY.Should().BeApproximately(0.25d, 1e-6);
        bounds.MaxY.Should().BeApproximately(0.75d, 1e-6);
    }

    [Fact]
    public void PlanNearThreeDimensionalThrows()
    {
        var engine = new Cartesian2DEngine("position");
        var center3D = new GeoPoint3D(0.5d, 0.5d, 0.5d);

        var action = () => engine.PlanNear(center3D, 0.1d);
        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void EnsurePointIndexBackfillsDocuments()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection("points");
        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 0.25d,
                ["y"] = 0.75d
            }
        });

        var descriptor = SpatialCartesian2D.EnsurePointIndex(database, "points", "position", new SpatialIndexOptions(precisionBits: 10));

        descriptor.EngineName.Should().Be(Cartesian2DEngine.EngineName);
        var stored = collection.FindById(1);
        ((object?)stored).Should().NotBeNull();
        stored![descriptor.Options.IndexFieldName].IsNull.Should().BeFalse();
        stored[descriptor.Options.BoundingBoxFieldName].AsArray.Count.Should().Be(4);
    }

    [Fact]
    public void PlanWithinRespectsProvidedBounds()
    {
        var engine = new Cartesian2DEngine("position");
        var bounds = BoundingBox.From2D(0.1d, 0.2d, 0.4d, 0.6d);
        var plan = engine.PlanWithin(bounds);

        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().Be(bounds);
    }
}
