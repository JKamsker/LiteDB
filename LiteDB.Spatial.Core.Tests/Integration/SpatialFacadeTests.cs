extern alias LiteDbBase;

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Integration;

public sealed class SpatialFacadeTests
{
    [Fact]
    public void GeographicFacade_Creates_Metadata_And_Executes_Queries()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var places = database.GetCollection<GeoPlace>("places");

        Spatial.UseGeographic(places, new SpatialIndexOptions(precisionBits: 10));
        Spatial.EnsurePointIndex(places, x => x.Location);

        places.Insert(new List<GeoPlace>
        {
            new GeoPlace { Name = "Origin", Location = new GeoPoint(0, 0) },
            new GeoPlace { Name = "Nearby", Location = new GeoPoint(0.01, 0.01) },
            new GeoPlace { Name = "Far", Location = new GeoPoint(0.5, 0.5) }
        });

        var metadata = new SpatialMetadataStore(database);
        var descriptor = metadata.GetRequiredDescriptor("places");
        descriptor.EngineName.Should().Be(GeographicEngine.EngineName);
        descriptor.GeometryFieldName.Should().Be("Location");

        var center = new GeoPoint(0, 0);
        var near = Spatial.Near(places, x => x.Location, center, radius: 5_000).ToList();
        near.Should().HaveCount(2);
        near.Select(x => x.Name).Should().Contain(new[] { "Origin", "Nearby" }).And.NotContain("Far");

        var bounds = BoundingBox.From2D(-0.05, -0.05, 0.05, 0.05);
        var inBox = Spatial.WithinBoundingBox(places, x => x.Location, bounds).ToList();
        inBox.Select(x => x.Name).Should().BeEquivalentTo(new[] { "Origin", "Nearby" });
    }

    [Fact]
    public void Cartesian2D_Facade_Uses_Domain_And_Plan()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var points = database.GetCollection<Cartesian2DPoint>("grid");

        var domain = BoundingBox.From2D(-10, -10, 10, 10);
        Spatial.UseCartesian2D(points, domain, new SpatialIndexOptions(precisionBits: 8));
        Spatial.EnsurePointIndex(points, x => x.Position);

        points.Insert(new List<Cartesian2DPoint>
        {
            new Cartesian2DPoint { Id = 1, Position = new GeoPoint(0, 0) },
            new Cartesian2DPoint { Id = 2, Position = new GeoPoint(1, 1) },
            new Cartesian2DPoint { Id = 3, Position = new GeoPoint(5, 5) }
        });

        var near = Spatial.Near(points, x => x.Position, new GeoPoint(0, 0), radius: 2).ToList();
        near.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });

        var bounds = BoundingBox.From2D(-1, -1, 2, 2);
        var inBox = Spatial.WithinBoundingBox(points, x => x.Position, bounds).ToList();
        inBox.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Cartesian3D_Facade_Supports_Near_And_Bounding_Box()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var points = database.GetCollection<Cartesian3DPoint>("cloud");

        var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
        Spatial.UseCartesian3D(points, domain, new SpatialIndexOptions(precisionBits: 10));
        Spatial.EnsurePointIndex(points, x => x.Position);

        points.Insert(new List<Cartesian3DPoint>
        {
            new Cartesian3DPoint { Id = 1, Position = new GeoPoint3D(0, 0, 0) },
            new Cartesian3DPoint { Id = 2, Position = new GeoPoint3D(1, 1, 1) },
            new Cartesian3DPoint { Id = 3, Position = new GeoPoint3D(10, 10, 10) }
        });

        var near = Spatial.Near(points, x => x.Position, new GeoPoint3D(0, 0, 0), radius: 3).ToList();
        near.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });

        var bounds = BoundingBox.From3D(-2, -2, -2, 2, 2, 2);
        var inBox = Spatial.WithinBoundingBox(points, x => x.Position, bounds).ToList();
        inBox.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    private sealed class GeoPlace
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public GeoPoint Location { get; set; }
    }

    private sealed class Cartesian2DPoint
    {
        public int Id { get; set; }
        public GeoPoint Position { get; set; }
    }

    private sealed class Cartesian3DPoint
    {
        public int Id { get; set; }
        public GeoPoint3D Position { get; set; }
    }
}
