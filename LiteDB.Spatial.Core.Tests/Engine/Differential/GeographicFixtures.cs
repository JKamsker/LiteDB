#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

internal sealed record GeodesicPair(string Id, GeoPoint From, GeoPoint To, double GeographicLibDistanceMeters)
{
    public static GeodesicPair FromDto(GeodesicPairDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        return new GeodesicPair(
            dto.Id ?? throw new ArgumentException("Geodesic pair is missing an id."),
            new GeoPoint(dto.From.Lon, dto.From.Lat),
            new GeoPoint(dto.To.Lon, dto.To.Lat),
            dto.GeographicLibDistanceMeters);
    }
}

internal sealed record GeodesicPairDto(string? Id, CoordinateDto From, CoordinateDto To, double GeographicLibDistanceMeters);

internal sealed record CoordinateDto(double Lon, double Lat);

internal sealed record BoundingBoxFixture(
    string Id,
    BoundingBox Bounds,
    IReadOnlyList<GeographicFixturePoint> Points)
{
    public static BoundingBoxFixture FromDto(BoundingBoxFixtureDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var bounds = BoundingBox.From2D(dto.Bounds.MinLon, dto.Bounds.MinLat, dto.Bounds.MaxLon, dto.Bounds.MaxLat);
        var points = dto.Points
            .Select(p => new GeographicFixturePoint(p.Id, new GeoPoint(p.Lon, p.Lat)))
            .ToArray();

        return new BoundingBoxFixture(
            dto.Id ?? throw new ArgumentException("Bounding box fixture requires an id."),
            bounds,
            points);
    }
}

internal sealed record BoundingBoxFixtureDto(
    string? Id,
    BoundingBoxDto Bounds,
    IReadOnlyList<PointDto> Points);

internal sealed record BoundingBoxDto(double MinLon, double MinLat, double MaxLon, double MaxLat);

internal sealed record GeographicFixturePoint(string Id, GeoPoint Location);

internal sealed record PointDto(string Id, double Lon, double Lat);

internal sealed record NearQueryFixture(
    string Id,
    GeoPoint Center,
    double RadiusMeters,
    IReadOnlyList<GeographicFixturePoint> Points)
{
    public static NearQueryFixture FromDto(NearQueryFixtureDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var points = dto.Points
            .Select(p => new GeographicFixturePoint(p.Id, new GeoPoint(p.Lon, p.Lat)))
            .ToArray();

        return new NearQueryFixture(
            dto.Id ?? throw new ArgumentException("Near fixture requires an id."),
            new GeoPoint(dto.Center.Lon, dto.Center.Lat),
            dto.RadiusMeters,
            points);
    }
}

internal sealed record NearQueryFixtureDto(
    string? Id,
    CoordinateDto Center,
    double RadiusMeters,
    IReadOnlyList<PointDto> Points);
