extern alias LiteDbBase;

using System.IO;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Facade;

public sealed class SpatialFacadeTests
{
    [Fact]
    public void UseGeographicEnablesNearQueries()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<City>("cities");

        Spatial.UseGeographic(collection, x => x.Location);

        var vienna = new City("Vienna", new GeoPoint(16.3738, 48.2082));
        var prague = new City("Prague", new GeoPoint(14.4378, 50.0755));
        var newYork = new City("New York", new GeoPoint(-73.935242, 40.73061));

        collection.Insert(new[] { vienna, prague, newYork });

        Spatial.EnsurePointIndex(collection);

        var results = Spatial.Near(collection, x => x.Location, new GeoPoint(16.3738, 48.2082), radius: 300_000);

        results.Should().Contain(c => c.Name == "Vienna");
        results.Should().Contain(c => c.Name == "Prague");
        results.Should().NotContain(c => c.Name == "New York");
    }

    [Fact]
    public void GeographicWithinBoundingBoxHonorsMetadata()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<City>("cities");

        Spatial.UseGeographic(collection, x => x.Location);

        var east = new City("East", new GeoPoint(-179.5, 0));
        var west = new City("West", new GeoPoint(179.5, 0));
        var far = new City("Far", new GeoPoint(150, 0));

        collection.Insert(new[] { east, west, far });

        Spatial.EnsurePointIndex(collection);

        var bounds = BoundingBox.From2D(170, -10, 190, 10);
        var results = Spatial.WithinBoundingBox(collection, x => x.Location, bounds);

        results.Should().Contain(c => c.Name == "East");
        results.Should().Contain(c => c.Name == "West");
        results.Should().NotContain(c => c.Name == "Far");
    }

    [Fact]
    public void EnsurePointIndexRebuildsIndexFields()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var typed = database.GetCollection<City>("cities");
        Spatial.UseGeographic(typed, x => x.Location);

        var city = new City("Center", new GeoPoint(0, 0));
        typed.Insert(city);

        var raw = database.GetCollection("cities");
        var stored = raw.FindOne(BaseLiteDB.Query.All());
        stored[SpatialIndexOptions.DefaultIndexFieldName] = BaseLiteDB.BsonValue.Null;
        raw.Update(stored);

        Spatial.EnsurePointIndex(typed);

        var updated = raw.FindById(stored["_id"]);
        updated[SpatialIndexOptions.DefaultIndexFieldName].IsNull.Should().BeFalse();
    }

    [Fact]
    public void Cartesian2DNearUsesConfiguredDomain()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<Point2DEntity>("points2d");

        Spatial.UseCartesian2D(collection, x => x.Position, BoundingBox.From2D(-100, -100, 100, 100));

        var origin = new Point2DEntity("Origin", new GeoPoint(0, 0));
        var neighbor = new Point2DEntity("Neighbor", new GeoPoint(1, 1));
        var distant = new Point2DEntity("Far", new GeoPoint(50, 50));

        collection.Insert(new[] { origin, neighbor, distant });

        Spatial.EnsurePointIndex(collection);

        var results = Spatial.Near(collection, x => x.Position, new GeoPoint(0, 0), radius: 5);

        results.Should().Contain(p => p.Name == "Origin");
        results.Should().Contain(p => p.Name == "Neighbor");
        results.Should().NotContain(p => p.Name == "Far");
    }

    [Fact]
    public void Cartesian3DNearFiltersCandidates()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<Point3DEntity>("points3d");

        Spatial.UseCartesian3D(collection, x => x.Position, BoundingBox.From3D(-100, -100, -100, 100, 100, 100));

        var origin = new Point3DEntity("Origin", new GeoPoint3D(0, 0, 0));
        var neighbor = new Point3DEntity("Neighbor", new GeoPoint3D(1, 1, 1));
        var distant = new Point3DEntity("Distant", new GeoPoint3D(20, 20, 20));

        collection.Insert(new[] { origin, neighbor, distant });

        Spatial.EnsurePointIndex(collection);

        var results = Spatial.Near(collection, x => x.Position, new GeoPoint3D(0, 0, 0), radius: 5);

        results.Should().Contain(p => p.Name == "Origin");
        results.Should().Contain(p => p.Name == "Neighbor");
        results.Should().NotContain(p => p.Name == "Distant");
    }

    [Fact]
    public void Cartesian3DClampsPrecisionToSupportedBits()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<Point3DEntity>("points3d");

        var descriptor = Spatial.UseCartesian3D(
            collection,
            x => x.Position,
            BoundingBox.From3D(-10, -10, -10, 10, 10, 10),
            new SpatialIndexOptions(precisionBits: 32));

        descriptor.Options.PrecisionBits.Should().Be(21);
    }

    private sealed record City(string Name, GeoPoint Location);

    private sealed record Point2DEntity(string Name, GeoPoint Position);

    private sealed record Point3DEntity(string Name, GeoPoint3D Position);
}
