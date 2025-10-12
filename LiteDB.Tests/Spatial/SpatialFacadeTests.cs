extern alias SpatialCore;
extern alias SpatialFacade;

using System;
using System.Linq;
using FluentAssertions;
using LiteDB.Tests;
using Xunit;
using LiteDatabase = LiteDB.LiteDatabase;
using SpatialApi = SpatialFacade::LiteDB.Spatial.Spatial;
using SpatialBoundingBox = SpatialCore::LiteDB.Spatial.BoundingBox;
using SpatialGeoPoint = SpatialCore::LiteDB.Spatial.GeoPoint;
using SpatialGeoPoint3D = SpatialCore::LiteDB.Spatial.GeoPoint3D;
using SpatialIndexOptions = SpatialCore::LiteDB.Spatial.SpatialIndexOptions;
using SpatialMetadataException = SpatialCore::LiteDB.Spatial.SpatialMetadataException;

namespace LiteDB.Tests.Spatial;

public sealed class SpatialFacadeTests
{
    [Fact]
    public void GeographicBoundingBoxHandlesAntiMeridianWrap()
    {
        using var tempFile = new TempFile();
        using var database = new LiteDatabase(tempFile.Filename);

        var places = database.GetCollection<Place>("places");
        SpatialApi.UseGeographic(places, x => x.Location);

        places.Insert(new[]
        {
            new Place { Id = 1, Name = "Nadi", Location = new SpatialGeoPoint(179.9, -16.5) },
            new Place { Id = 2, Name = "Apia", Location = new SpatialGeoPoint(-179.8, -13.8) },
            new Place { Id = 3, Name = "London", Location = new SpatialGeoPoint(-0.1, 51.5) }
        });

        SpatialApi.EnsurePointIndex(places);

        var antiMeridianBounds = SpatialBoundingBox.From2D(170, -20, 190, 10);
        var matches = SpatialApi.WithinBoundingBox(places, x => x.Location, antiMeridianBounds)
            .Select(x => x.Id)
            .ToArray();

        matches.Should().Contain(new[] { 1, 2 });
        matches.Should().NotContain(3);
    }

    [Fact]
    public void CartesianNearHonorsDistanceTolerance()
    {
        using var tempFile = new TempFile();
        using var database = new LiteDatabase(tempFile.Filename);

        var readings = database.GetCollection<CartesianPoint>("readings");
        var options = new SpatialIndexOptions(distanceTolerance: 1);
        var domain = SpatialBoundingBox.From2D(-100, -100, 100, 100);

        SpatialApi.UseCartesian2D(readings, x => x.Position, domain, options);

        readings.Insert(new[]
        {
            new CartesianPoint { Id = 1, Position = new SpatialGeoPoint(0, 0) },
            new CartesianPoint { Id = 2, Position = new SpatialGeoPoint(3, 4) }, // Distance 5
            new CartesianPoint { Id = 3, Position = new SpatialGeoPoint(20, 20) }
        });

        SpatialApi.EnsurePointIndex(readings);

        var matches = SpatialApi.Near(readings, x => x.Position, new SpatialGeoPoint(0, 0), radius: 4.7)
            .Select(x => x.Id)
            .ToArray();

        matches.Should().Contain(new[] { 1, 2 });
        matches.Should().NotContain(3);
    }

    [Fact]
    public void Cartesian3DWithinBoundingBoxRejectsDimensionMismatch()
    {
        using var tempFile = new TempFile();
        using var database = new LiteDatabase(tempFile.Filename);

        var points = database.GetCollection<CartesianPoint3D>("points");
        var domain = SpatialBoundingBox.From3D(-10, -10, -10, 10, 10, 10);

        SpatialApi.UseCartesian3D(points, x => x.Position, domain);

        points.Insert(new[]
        {
            new CartesianPoint3D { Id = 1, Position = new SpatialGeoPoint3D(0, 0, 0) }
        });

        SpatialApi.EnsurePointIndex(points);

        var invalidBounds = SpatialBoundingBox.From2D(-5, -5, 5, 5);
        Action act = () => SpatialApi.WithinBoundingBox(points, x => x.Position, invalidBounds);

        act.Should().Throw<SpatialMetadataException>()
            .WithMessage("*3D*");
    }

    private sealed class Place
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public SpatialGeoPoint Location { get; set; }
    }

    private sealed class CartesianPoint
    {
        public int Id { get; set; }

        public SpatialGeoPoint Position { get; set; }
    }

    private sealed class CartesianPoint3D
    {
        public int Id { get; set; }

        public SpatialGeoPoint3D Position { get; set; }
    }
}
