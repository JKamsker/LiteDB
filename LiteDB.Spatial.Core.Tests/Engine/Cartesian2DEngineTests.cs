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
    public void PlanNearProducesNormalizedBounds()
    {
        var domain = BoundingBox.From2D(0, 0, 100, 100);
        var options = new SpatialIndexOptions(precisionBits: 10, maxCoveringCells: 64);
        var engine = new Cartesian2DEngine("position", domain, options);

        var plan = engine.PlanNear(new GeoPoint(10, 10), 5);

        plan.Dimensions.Should().Be(2);
        plan.CoveringBounds.Should().NotBeNull();
        var covering = plan.CoveringBounds!.Value;
        covering.MinX.Should().BeApproximately(5, 1e-6);
        covering.MaxX.Should().BeApproximately(15, 1e-6);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void PlanWithinNormalizesBoundingBox()
    {
        var domain = BoundingBox.From2D(-10, -10, 10, 10);
        var engine = new Cartesian2DEngine("position", domain, new SpatialIndexOptions(precisionBits: 8));

        var bounds = BoundingBox.From2D(-5, -5, 5, 5);
        var plan = engine.PlanWithin(bounds);

        plan.CoveringBounds.Should().NotBeNull();
        var covering = plan.CoveringBounds!.Value;
        covering.MinX.Should().BeApproximately(-5, 1e-6);
        covering.MinY.Should().BeApproximately(-5, 1e-6);
    }

    [Fact]
    public void PlanNearRejectsNegativeRadius()
    {
        var domain = BoundingBox.From2D(0, 0, 1, 1);
        var engine = new Cartesian2DEngine("position", domain, new SpatialIndexOptions());

        var action = () => engine.PlanNear(new GeoPoint(0.5, 0.5), -1);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SpatialCartesian2DEnsurePointIndexAttachesEngine()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("cart2d");
        var metadata = new SpatialMetadataStore(database);
        var domain = BoundingBox.From2D(0, 0, 10, 10);

        collection.Insert(new BaseLiteDB.BsonDocument
        {
            ["_id"] = 1,
            ["position"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 1.5,
                ["y"] = 2.5
            }
        });

        var descriptor = SpatialCartesian2D.EnsurePointIndex(metadata, collection, "position", domain, new SpatialIndexOptions(precisionBits: 6));

        descriptor.Engine.Should().BeOfType<Cartesian2DEngine>();
        descriptor.Settings.Domain.Should().Be(domain);

        var plan = SpatialCartesian2D.Near(descriptor, new GeoPoint(5, 5), 1);
        plan.IndexRanges.Should().NotBeEmpty();

        var stored = collection.FindById(new BaseLiteDB.BsonValue(1));
        ((object?)stored).Should().NotBeNull();
        var bounding = stored![descriptor.Options.BoundingBoxFieldName].AsArray;
        bounding.Count.Should().Be(4);
        bounding[0].AsDouble.Should().BeApproximately(1.5, 1e-6);
        bounding[1].AsDouble.Should().BeApproximately(2.5, 1e-6);
    }
}
