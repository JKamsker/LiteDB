#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class PointCloudFixtures
{
    private const string RegenerateEnvironmentVariable = "SPATIAL_REGENERATE_FIXTURES";

    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly IReadOnlyList<PointCloudDefinition> _definitions = new[]
    {
        PointCloudDefinition.Create(
            "unit_grid",
            BoundingBox.From2D(-10, -10, 10, 10),
            static domain =>
            {
                var points = new List<PointDocument>();
                var positions = new[] { -8d, -4d, 0d, 4d, 8d };
                var id = 1;

                foreach (var x in positions)
                {
                    foreach (var y in positions)
                    {
                        points.Add(new PointDocument(id++, x, y));
                    }
                }

                points.Add(new PointDocument(id++, -1.5, 7.25));
                points.Add(new PointDocument(id++, 3.25, -6.75));
                points.Add(new PointDocument(id++, 6.9, 1.4));
                points.Add(new PointDocument(id++, -7.4, -2.2));

                return points;
            },
            new[]
            {
                new QueryDefinition("origin-5", new GeoPoint(0, 0), 5.0),
                new QueryDefinition("upper-right-4", new GeoPoint(6, 6), 4.0),
                new QueryDefinition("southern-band-3", new GeoPoint(0, -6), 3.5)
            }),
        PointCloudDefinition.Create(
            "clustered",
            BoundingBox.From2D(-12, -12, 12, 12),
            static domain =>
            {
                var points = new List<PointDocument>();
                var id = 1;

                foreach (var offset in BuildCluster(-6.0, -6.0, 0.9, 0.7))
                {
                    points.Add(new PointDocument(id++, offset.x, offset.y));
                }

                foreach (var offset in BuildCluster(5.5, 4.5, 0.8, 0.6))
                {
                    points.Add(new PointDocument(id++, offset.x, offset.y));
                }

                foreach (var offset in BuildCluster(-2.5, 7.5, 0.6, 0.9))
                {
                    points.Add(new PointDocument(id++, offset.x, offset.y));
                }

                points.Add(new PointDocument(id++, 0, 0));
                points.Add(new PointDocument(id++, -1.75, 1.25));
                points.Add(new PointDocument(id++, 7.75, -1.25));
                points.Add(new PointDocument(id++, 4.25, -8.5));

                return points;
            },
            new[]
            {
                new QueryDefinition("cluster-a-2", new GeoPoint(-6.0, -6.0), 2.25),
                new QueryDefinition("diagonal-6", new GeoPoint(0, 0), 6.0),
                new QueryDefinition("east-ridge-3", new GeoPoint(6.0, 4.5), 3.25)
            })
    };

    public static PointCloudFixture Load(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Fixture name must be provided.", nameof(name));
        }

        var definition = _definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Unknown point cloud fixture '{name}'.");

        var path = TestPathHelpers.ResolveRelativeToRepo("tests", "fixtures", "point_clouds", name + ".json");
        EnsureFixture(definition, path);

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<FixtureDocument>(stream, _serializerOptions)
            ?? throw new InvalidOperationException($"Fixture '{name}' is missing or malformed.");

        return document.ToRuntime();
    }

    public static IReadOnlyList<PointCloudFixture> LoadAll()
    {
        return _definitions.Select(definition => Load(definition.Name)).ToList();
    }

    private static void EnsureFixture(PointCloudDefinition definition, string path)
    {
        var regenerate = ShouldRegenerate();
        if (regenerate || !File.Exists(path))
        {
            var document = definition.Generate();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, document, _serializerOptions);
        }
    }

    private static bool ShouldRegenerate()
    {
        var value = Environment.GetEnvironmentVariable(RegenerateEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(double x, double y)> BuildCluster(double centerX, double centerY, double horizontalStep, double verticalStep)
    {
        var offsets = new (int x, int y)[]
        {
            (0, 0),
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1),
            (-1, -1),
            (1, 1),
            (-1, 1),
            (1, -1)
        };

        foreach (var (offsetX, offsetY) in offsets)
        {
            yield return (centerX + offsetX * horizontalStep, centerY + offsetY * verticalStep);
        }
    }

    private sealed record PointCloudDefinition(
        string Name,
        BoundingBox Domain,
        Func<BoundingBox, IReadOnlyList<PointDocument>> BuildPoints,
        IReadOnlyList<QueryDefinition> Queries)
    {
        public static PointCloudDefinition Create(
            string name,
            BoundingBox domain,
            Func<BoundingBox, IReadOnlyList<PointDocument>> pointFactory,
            IReadOnlyList<QueryDefinition> queries)
        {
            return new PointCloudDefinition(name, domain, pointFactory, queries);
        }

        public FixtureDocument Generate()
        {
            var points = BuildPoints(Domain);
            var document = new FixtureDocument
            {
                Name = Name,
                Domain = DomainDocument.From(Domain),
                Points = points.ToList(),
                Queries = Queries.Select(QueryDocument.From).ToList()
            };

            return document;
        }
    }

    private sealed record QueryDefinition(string Name, GeoPoint Center, double Radius);

    private sealed record PointDocument(int Id, double X, double Y)
    {
        public PointSample ToSample()
        {
            return new PointSample(Id, new GeoPoint(X, Y));
        }
    }

    private sealed record CoordinateDocument(double X, double Y)
    {
        public GeoPoint ToPoint()
        {
            return new GeoPoint(X, Y);
        }

        public static CoordinateDocument From(GeoPoint point)
        {
            return new CoordinateDocument(point.Longitude, point.Latitude);
        }
    }

    private sealed record DomainDocument(double MinX, double MinY, double MaxX, double MaxY)
    {
        public BoundingBox ToBoundingBox()
        {
            return BoundingBox.From2D(MinX, MinY, MaxX, MaxY);
        }

        public static DomainDocument From(BoundingBox bounds)
        {
            return new DomainDocument(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
        }
    }

    private sealed record QueryDocument(string Name, CoordinateDocument Center, double Radius)
    {
        public NearQuery ToRuntime()
        {
            return new NearQuery(Name, Center.ToPoint(), Radius);
        }

        public static QueryDocument From(QueryDefinition definition)
        {
            return new QueryDocument(definition.Name, CoordinateDocument.From(definition.Center), definition.Radius);
        }
    }

    private sealed record FixtureDocument
    {
        public required string Name { get; init; }

        public required DomainDocument Domain { get; init; }

        public required List<PointDocument> Points { get; init; }

        public required List<QueryDocument> Queries { get; init; }

        public PointCloudFixture ToRuntime()
        {
            var samples = Points.Select(point => point.ToSample()).ToList();
            var queries = Queries.Select(query => query.ToRuntime()).ToList();
            return new PointCloudFixture(Name, Domain.ToBoundingBox(), samples, queries);
        }
    }
}
