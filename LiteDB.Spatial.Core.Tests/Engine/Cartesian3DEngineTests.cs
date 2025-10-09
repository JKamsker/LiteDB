extern alias LiteDbBase;

#nullable enable

using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class Cartesian3DEngineTests
{
    [Fact]
    public void PlanNearProducesThreeDimensionalPredicate()
    {
        var engine = new LiteDB.Spatial.Cartesian3DEngine("location", new LiteDB.Spatial.SpatialIndexOptions(distanceTolerance: 0.5));
        var plan = engine.PlanNear(new LiteDB.Spatial.GeoPoint3D(0.25, 0.5, 0.75), 0.2);

        plan.Dimensions.Should().Be(3);
        plan.CoveringBounds.Should().NotBeNull();
        plan.ExactPredicateDescription.Should().Contain("Euclidean3D");
    }

    [Fact]
    public void MapperReadsArrayCoordinates()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(3, 8);
        var mapper = new LiteDB.Spatial.Cartesian3DMapper(encoder, "location");
        var document = new BaseLiteDB.BsonDocument
        {
            ["location"] = new BaseLiteDB.BsonArray
            {
                new BaseLiteDB.BsonValue(0.1),
                new BaseLiteDB.BsonValue(0.2),
                new BaseLiteDB.BsonValue(0.3)
            }
        };

        mapper.TryReadPoint(document, out LiteDB.Spatial.GeoPoint3D point).Should().BeTrue();
        point.X.Should().Be(0.1);
        point.Y.Should().Be(0.2);
        point.Z.Should().Be(0.3);
    }

    [Fact]
    public void SpatialCartesian3DEnsuresMetadata()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        LiteDB.Spatial.SpatialCartesian3D.EnsurePointIndex(database, "cloud", "position", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 6));

        var plan = LiteDB.Spatial.SpatialCartesian3D.Near(database, "cloud", new LiteDB.Spatial.GeoPoint3D(0d, 0d, 0d), 0.5);
        plan.IndexRanges.Should().NotBeEmpty();
    }
}
