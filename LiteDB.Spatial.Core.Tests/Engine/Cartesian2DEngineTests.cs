extern alias LiteDbBase;

#nullable enable

using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class Cartesian2DEngineTests
{
    [Fact]
    public void PlanNearProducesEuclideanPredicate()
    {
        var engine = new LiteDB.Spatial.Cartesian2DEngine("location", new LiteDB.Spatial.SpatialIndexOptions(distanceTolerance: 0.25));
        var plan = engine.PlanNear(new LiteDB.Spatial.GeoPoint(0.5, 0.5), 0.1);

        plan.Dimensions.Should().Be(2);
        plan.CoveringBounds.Should().NotBeNull();
        plan.ExactPredicateDescription.Should().Contain("Euclidean2D");
    }

    [Fact]
    public void MapperReadsDocumentCoordinates()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(2, 8);
        var mapper = new LiteDB.Spatial.Cartesian2DMapper(encoder, "location");
        var document = new BaseLiteDB.BsonDocument
        {
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["x"] = 0.25,
                ["y"] = 0.75
            }
        };

        mapper.TryReadPoint(document, out LiteDB.Spatial.GeoPoint point).Should().BeTrue();
        point.Longitude.Should().Be(0.25);
        point.Latitude.Should().Be(0.75);
    }

    [Fact]
    public void SpatialCartesian2DEnsuresMetadata()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        LiteDB.Spatial.SpatialCartesian2D.EnsurePointIndex(database, "points", "location", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 8));

        var plan = LiteDB.Spatial.SpatialCartesian2D.Near(database, "points", new LiteDB.Spatial.GeoPoint(0.5, 0.5), 0.1);
        plan.IndexRanges.Should().NotBeEmpty();
    }
}
