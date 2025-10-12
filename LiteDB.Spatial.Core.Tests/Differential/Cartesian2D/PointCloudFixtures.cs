#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB.Spatial.Core.Tests.TestSupport;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

internal sealed record PointCloudFixture(string Id, BoundingBox Domain, IReadOnlyList<PointSample> Points, IReadOnlyList<PointQuery> Queries)
{
    public static PointCloudFixture Load(string relativePath)
    {
        var path = TestResourceLocator.GetFixturePath(relativePath);
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<PointCloudDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException($"Fixture '{path}' could not be deserialized.");

        var domain = BoundingBox.From2D(dto.Domain.MinX, dto.Domain.MinY, dto.Domain.MaxX, dto.Domain.MaxY);
        var points = dto.Points.Select(p => new PointSample(p.Label, new CartesianPoint2D(p.X, p.Y))).ToList();
        var queries = dto.Queries.Select(q => new PointQuery(q.Id, new CartesianPoint2D(q.Center.X, q.Center.Y), q.Radius)).ToList();
        return new PointCloudFixture(dto.Id ?? Path.GetFileNameWithoutExtension(relativePath), domain, points, queries);
    }
}

internal sealed record PointSample(string Label, CartesianPoint2D Position);

internal sealed record PointQuery(string Id, CartesianPoint2D Center, double Radius);

internal sealed class PointCloudDto
{
    public string? Id { get; set; }

    public DomainDto Domain { get; set; } = null!;

    public List<PointDto> Points { get; set; } = new();

    public List<QueryDto> Queries { get; set; } = new();
}

internal sealed class DomainDto
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
}

internal sealed class PointDto
{
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
}

internal sealed class QueryDto
{
    public string Id { get; set; } = string.Empty;
    public CenterDto Center { get; set; } = new();
    public double Radius { get; set; }
}

internal sealed class CenterDto
{
    public double X { get; set; }
    public double Y { get; set; }
}
