#nullable enable
using System.Text.Json.Serialization;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

public sealed record GeoEndpoint(
    [property: JsonPropertyName("lon")] double Lon,
    [property: JsonPropertyName("lat")] double Lat);

public sealed record GeodesicPair(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("from")] GeoEndpoint From,
    [property: JsonPropertyName("to")] GeoEndpoint To,
    [property: JsonPropertyName("distance_m")] double DistanceM);

public sealed record GeoJsonPolygonFixture(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("description")] string? Description,
    string RawJson);

public sealed record PointCloud2D(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("points")] double[][] Points);

public sealed record PointCloud3D(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("points")] double[][] Points);
