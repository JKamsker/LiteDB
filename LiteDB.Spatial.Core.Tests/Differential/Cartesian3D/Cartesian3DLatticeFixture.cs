using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

public sealed class Cartesian3DLatticeFixture
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Cartesian3DLatticeFixture(
        string name,
        BoundingBox domain,
        SpatialIndexOptions options,
        IReadOnlyList<Cartesian3DLatticePoint> points,
        IReadOnlyList<Cartesian3DNearQuery> nearQueries,
        IReadOnlyList<Cartesian3DAabbQuery> aabbQueries)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Domain = domain;
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Points = points ?? throw new ArgumentNullException(nameof(points));
        NearQueries = nearQueries ?? throw new ArgumentNullException(nameof(nearQueries));
        AabbQueries = aabbQueries ?? throw new ArgumentNullException(nameof(aabbQueries));
    }

    public string Name { get; }

    public BoundingBox Domain { get; }

    public SpatialIndexOptions Options { get; }

    public IReadOnlyList<Cartesian3DLatticePoint> Points { get; }

    public IReadOnlyList<Cartesian3DNearQuery> NearQueries { get; }

    public IReadOnlyList<Cartesian3DAabbQuery> AabbQueries { get; }

    public static Cartesian3DLatticeFixture LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Fixture path must be provided.", nameof(path));
        }

        var json = File.ReadAllText(path);
        var name = Path.GetFileNameWithoutExtension(path);
        return FromJson(name, json);
    }

    public static Cartesian3DLatticeFixture FromJson(string fallbackName, string json)
    {
        var raw = JsonSerializer.Deserialize<RawFixture>(json, SerializerOptions) ?? throw new InvalidOperationException("Unable to deserialize lattice fixture.");
        var domain = BoundingBox.From3D(
            raw.Domain.Min[0],
            raw.Domain.Min[1],
            raw.Domain.Min[2],
            raw.Domain.Max[0],
            raw.Domain.Max[1],
            raw.Domain.Max[2]);

        var options = new SpatialIndexOptions(
            raw.Options?.PrecisionBits ?? 32,
            raw.Options?.MaxCoveringCells ?? 64,
            raw.Options?.DistanceTolerance ?? 0.001,
            raw.Options?.IndexFieldName ?? SpatialIndexOptions.DefaultIndexFieldName,
            raw.Options?.BoundingBoxFieldName ?? SpatialIndexOptions.DefaultBoundingBoxFieldName);

        var points = raw.Points
            .Select(p => new Cartesian3DLatticePoint(p.Id, p.X, p.Y, p.Z))
            .OrderBy(p => p.Id)
            .ToList();

        var near = (raw.NearQueries ?? Array.Empty<RawNearQuery>())
            .Select(q => new Cartesian3DNearQuery(q.Id ?? string.Empty, new GeoPoint3D(q.Center[0], q.Center[1], q.Center[2]), q.Radius, q.ExpectCapped))
            .ToList();

        var boxes = (raw.AabbQueries ?? Array.Empty<RawAabbQuery>())
            .Select(b => new Cartesian3DAabbQuery(b.Id ?? string.Empty, BoundingBox.From3D(b.Bounds[0], b.Bounds[1], b.Bounds[2], b.Bounds[3], b.Bounds[4], b.Bounds[5])))
            .ToList();

        var name = string.IsNullOrWhiteSpace(raw.Name) ? fallbackName : raw.Name!;
        return new Cartesian3DLatticeFixture(name, domain, options, points, near, boxes);
    }

    private sealed class RawFixture
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("domain")]
        public RawDomain Domain { get; set; } = new();

        [JsonPropertyName("options")]
        public RawOptions? Options { get; set; }

        [JsonPropertyName("points")]
        public RawPoint[] Points { get; set; } = Array.Empty<RawPoint>();

        [JsonPropertyName("nearQueries")]
        public RawNearQuery[]? NearQueries { get; set; }

        [JsonPropertyName("aabbQueries")]
        public RawAabbQuery[]? AabbQueries { get; set; }
    }

    private sealed class RawDomain
    {
        [JsonPropertyName("min")]
        public double[] Min { get; set; } = Array.Empty<double>();

        [JsonPropertyName("max")]
        public double[] Max { get; set; } = Array.Empty<double>();
    }

    private sealed class RawOptions
    {
        [JsonPropertyName("precisionBits")]
        public int? PrecisionBits { get; set; }

        [JsonPropertyName("maxCoveringCells")]
        public int? MaxCoveringCells { get; set; }

        [JsonPropertyName("distanceTolerance")]
        public double? DistanceTolerance { get; set; }

        [JsonPropertyName("indexFieldName")]
        public string? IndexFieldName { get; set; }

        [JsonPropertyName("boundingBoxFieldName")]
        public string? BoundingBoxFieldName { get; set; }
    }

    private sealed class RawPoint
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }

    private sealed class RawNearQuery
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("center")]
        public double[] Center { get; set; } = Array.Empty<double>();

        [JsonPropertyName("radius")]
        public double Radius { get; set; }

        [JsonPropertyName("expectCapped")]
        public bool ExpectCapped { get; set; }
    }

    private sealed class RawAabbQuery
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("bounds")]
        public double[] Bounds { get; set; } = Array.Empty<double>();
    }
}

public sealed record Cartesian3DLatticePoint(int Id, double X, double Y, double Z)
{
    public GeoPoint3D ToGeoPoint() => new(X, Y, Z);
}

public sealed record Cartesian3DNearQuery(string Id, GeoPoint3D Center, double Radius, bool ExpectCapped);

public sealed record Cartesian3DAabbQuery(string Id, BoundingBox Bounds);
