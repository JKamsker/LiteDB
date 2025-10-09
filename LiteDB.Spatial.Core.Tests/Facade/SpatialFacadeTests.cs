extern alias LiteDbBase;
extern alias SpatialFacade;

#nullable enable

using System.IO;
using System.Linq;
using FluentAssertions;
using BaseLiteDB = LiteDbBase::LiteDB;
using FacadeSpatial = SpatialFacade::LiteDB.Spatial.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Facade;

public sealed class SpatialFacadeTests
{
    [Fact]
    public void UseGeographicNearReturnsOrderedResults()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<GeoPlace>("places");

        FacadeSpatial.UseGeographic(collection, x => x.Location, new SpatialIndexOptions(precisionBits: 12));

        var vienna = new GeoPoint(16.3738, 48.2082);
        collection.Insert(new GeoPlace { Name = "Stephansdom", Location = new GeoPoint(16.3725, 48.2086) });
        collection.Insert(new GeoPlace { Name = "Belvedere", Location = new GeoPoint(16.3808, 48.1910) });
        collection.Insert(new GeoPlace { Name = "Schonbrunn", Location = new GeoPoint(16.3119, 48.1845) });

        var results = FacadeSpatial.Near(collection, x => x.Location, vienna, radius: 2_000);

        results.Should().HaveCount(2);
        results.Select(x => x.Name).Should().ContainInOrder("Stephansdom", "Belvedere");

        var metadataCollection = database.GetCollection("_spatial_meta");
        metadataCollection.Count().Should().Be(1);
    }

    [Fact]
    public void GeographicNearHonorsDistanceTolerance()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<GeoPlace>("places");

        FacadeSpatial.UseGeographic(collection, x => x.Location, new SpatialIndexOptions(distanceTolerance: 250));

        var center = new GeoPoint(0, 0);
        collection.Insert(new GeoPlace { Name = "Origin", Location = center });
        collection.Insert(new GeoPlace { Name = "Edge", Location = new GeoPoint(0, 0.0092) });
        collection.Insert(new GeoPlace { Name = "Far", Location = new GeoPoint(0, 0.05) });

        var results = FacadeSpatial.Near(collection, x => x.Location, center, radius: 1_000);

        results.Select(x => x.Name).Should().Contain(new[] { "Origin", "Edge" });
        results.Should().NotContain(place => place.Name == "Far");
    }

    [Fact]
    public void GeographicWithinBoundingBoxSupportsAntiMeridian()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<GeoPlace>("places");

        FacadeSpatial.UseGeographic(collection, x => x.Location);

        collection.Insert(new GeoPlace { Name = "Eastern", Location = new GeoPoint(179.5, 5) });
        collection.Insert(new GeoPlace { Name = "Western", Location = new GeoPoint(-179.5, -3) });
        collection.Insert(new GeoPlace { Name = "Outside", Location = new GeoPoint(150, 0) });

        var window = BoundingBox.From2D(170, -10, 190, 10);
        var matches = FacadeSpatial.WithinBoundingBox(collection, x => x.Location, window);

        matches.Should().HaveCount(2);
        matches.Select(x => x.Name).Should().BeEquivalentTo(new[] { "Eastern", "Western" });
    }

    [Fact]
    public void Cartesian3DWithinBoundingBoxFiltersMatches()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<CartesianPoint>("points");

        var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
        FacadeSpatial.UseCartesian3D(collection, x => x.Position, domain, new SpatialIndexOptions(precisionBits: 10));

        collection.Insert(new CartesianPoint { Name = "Origin", Position = new GeoPoint3D(0, 0, 0) });
        collection.Insert(new CartesianPoint { Name = "Far", Position = new GeoPoint3D(50, 50, 50) });

        var window = BoundingBox.From3D(-10, -10, -10, 10, 10, 10);
        var results = FacadeSpatial.WithinBoundingBox(collection, x => x.Position, window);

        results.Should().ContainSingle(p => p.Name == "Origin");
        results.Should().NotContain(p => p.Name == "Far");
    }

    [Fact]
    public void Cartesian3DNearAppliesToleranceForBoundaryPoints()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<CartesianPoint>("points");

        var domain = BoundingBox.From3D(-1_000, -1_000, -1_000, 1_000, 1_000, 1_000);
        FacadeSpatial.UseCartesian3D(collection, x => x.Position, domain, new SpatialIndexOptions(distanceTolerance: 0.5));

        var center = new GeoPoint3D(0, 0, 0);
        collection.Insert(new CartesianPoint { Name = "Edge", Position = new GeoPoint3D(100, 0, 0) });
        collection.Insert(new CartesianPoint { Name = "Almost", Position = new GeoPoint3D(100.4, 0, 0) });
        collection.Insert(new CartesianPoint { Name = "Far", Position = new GeoPoint3D(300, 0, 0) });

        var nearResults = FacadeSpatial.Near(collection, x => x.Position, center, radius: 100);

        nearResults.Select(x => x.Name).Should().ContainInOrder("Edge", "Almost");
        nearResults.Should().NotContain(point => point.Name == "Far");

        var wideResults = FacadeSpatial.Near(collection, x => x.Position, center, radius: 1_000_000);
        wideResults.Should().HaveCount(3);
    }

    private sealed class GeoPlace
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPoint Location { get; set; } = new GeoPoint(0, 0);
    }

    private sealed class CartesianPoint
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPoint3D Position { get; set; } = new GeoPoint3D(0, 0, 0);
    }
}
