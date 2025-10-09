extern alias LiteDbBase;

#nullable enable

using System.IO;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Engine;

public sealed class GeographicEngineTests
{
    [Fact]
    public void PlanNearProducesVincentyPredicate()
    {
        var engine = new LiteDB.Spatial.GeographicEngine("location", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 16, distanceTolerance: 5));
        var plan = engine.PlanNear(new LiteDB.Spatial.GeoPoint(13.4050, 52.5200), 1_000d);

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("Vincenty");
        plan.CoveringBounds.Should().NotBeNull();
    }

    [Fact]
    public void PlanWithinHandlesAntimeridian()
    {
        var engine = new LiteDB.Spatial.GeographicEngine("location");
        var plan = engine.PlanWithin(LiteDB.Spatial.BoundingBox.From2D(170d, -10d, 190d, 10d));

        plan.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void MapperNormalizesCoordinatesAndEncodes()
    {
        var encoder = new LiteDB.Spatial.MortonIndexEncoder(2, 16);
        var mapper = new LiteDB.Spatial.GeographicMapper(encoder, "location");
        var document = new BaseLiteDB.BsonDocument
        {
            ["location"] = new BaseLiteDB.BsonDocument
            {
                ["longitude"] = 190d,
                ["latitude"] = 95d
            }
        };

        mapper.TryReadPoint(document, out LiteDB.Spatial.GeoPoint point).Should().BeTrue();
        point.Longitude.Should().BeApproximately(-170d, 1e-6);
        point.Latitude.Should().BeLessThan(90d);

        var bounding = mapper.GetBoundingBox(point);
        bounding.MinX.Should().BeGreaterOrEqualTo(0d);
        bounding.MaxX.Should().BeLessOrEqualTo(1d);

        mapper.Invoking(m => m.Encode(point)).Should().NotThrow();
    }

    [Fact]
    public void GeographicDistanceVincentyMatchesHaversineWithinTolerance()
    {
        var left = new LiteDB.Spatial.GeoPoint(-0.1278, 51.5074); // London
        var right = new LiteDB.Spatial.GeoPoint(2.3522, 48.8566); // Paris

        var haversine = new LiteDB.Spatial.GeographicDistance(LiteDB.Spatial.GeographicDistanceMode.Haversine);
        var vincenty = new LiteDB.Spatial.GeographicDistance(LiteDB.Spatial.GeographicDistanceMode.Vincenty);

        var d1 = haversine.Distance(left, right);
        var d2 = vincenty.Distance(left, right);

        d2.Should().BeApproximately(343_556d, 500d);
        d1.Should().BeApproximately(d2, 750d);
    }

    [Fact]
    public void SpatialGeographicFacadePersistsDescriptor()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var descriptor = LiteDB.Spatial.SpatialGeographic.EnsurePointIndex(database, "places", "location", new LiteDB.Spatial.SpatialIndexOptions(precisionBits: 10));

        descriptor.EngineName.Should().Be("Geographic");

        var plan = LiteDB.Spatial.SpatialGeographic.Near(database, "places", new LiteDB.Spatial.GeoPoint(0d, 0d), 500d);
        plan.IndexRanges.Should().NotBeEmpty();
    }
}
