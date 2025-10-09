using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Support;

public sealed class PointCloudFixture
{
    public required string Name { get; init; }
    public required BoundingBox Domain { get; init; }
    public required IReadOnlyList<PointSample> Points { get; init; }
    public required IReadOnlyList<QuerySample> Queries { get; init; }

    public static PointCloudFixture Load(string path)
    {
        var absolutePath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", path));

        using var stream = File.OpenRead(absolutePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var document = JsonSerializer.Deserialize<FixtureDocument>(stream, options) ?? throw new InvalidDataException($"Failed to load point cloud fixture '{path}'.");

        var domain = BoundingBox.From2D(document.Domain.MinX, document.Domain.MinY, document.Domain.MaxX, document.Domain.MaxY);
        var points = document.Points.Select(p => new PointSample(p.Id, p.X, p.Y)).ToList();
        var queries = document.Queries.Select(q => new QuerySample(q.Id, q.Center.X, q.Center.Y, q.Radius)).ToList();

        return new PointCloudFixture
        {
            Name = document.Name,
            Domain = domain,
            Points = points,
            Queries = queries
        };
    }

    private sealed class FixtureDocument
    {
        public string Name { get; set; } = string.Empty;
        public DomainDocument Domain { get; set; } = new();
        public List<PointDocument> Points { get; set; } = new();
        public List<QueryDocument> Queries { get; set; } = new();
    }

    private sealed class DomainDocument
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }

    private sealed class PointDocument
    {
        public string Id { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
    }

    private sealed class QueryDocument
    {
        public string Id { get; set; } = string.Empty;
        public CenterDocument Center { get; set; } = new();
        public double Radius { get; set; }
    }

    private sealed class CenterDocument
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}

public sealed record PointSample(string Id, double X, double Y)
{
    public GeoPoint ToPoint() => new(X, Y);
}

public sealed record QuerySample(string Id, double CenterX, double CenterY, double Radius)
{
    public GeoPoint Center => new(CenterX, CenterY);
}
