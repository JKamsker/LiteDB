#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LiteDB.Spatial;
using LiteDB.Spatial.Testing.Oracles;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

internal sealed record GeoCoordinateFixture
{
    public string? Id { get; init; }

    public double Lat { get; init; }

    public double Lon { get; init; }

    public GeodeticCoordinate ToCoordinate()
    {
        return new GeodeticCoordinate(Lat, Lon);
    }

    public GeoPoint ToGeoPoint()
    {
        return new GeoPoint(Lon, Lat);
    }
}

internal sealed record GeodesicPairFixture
{
    public string Id { get; init; } = string.Empty;

    public GeoCoordinateFixture From { get; init; } = new();

    public GeoCoordinateFixture To { get; init; } = new();

    [JsonPropertyName("distance_m")]
    public double DistanceM { get; init; }
}

internal sealed record PointCloud2DFixture
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<Point2DFixture> Points { get; init; } = Array.Empty<Point2DFixture>();
}

internal sealed record Point2DFixture
{
    public string Id { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public GeoPoint ToGeoPoint()
    {
        return new GeoPoint(X, Y);
    }
}

internal sealed record PointCloud3DFixture
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<Point3DFixture> Points { get; init; } = Array.Empty<Point3DFixture>();
}

internal sealed record Point3DFixture
{
    public string Id { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }

    public GeoPoint3D ToGeoPoint()
    {
        return new GeoPoint3D(X, Y, Z);
    }

    public CartesianCoordinate3D ToCartesian()
    {
        return new CartesianCoordinate3D(X, Y, Z);
    }
}
