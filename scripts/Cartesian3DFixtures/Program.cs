using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB.Spatial;

var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
var options = new SpatialIndexOptions(precisionBits: 6, maxCoveringCells: 4, distanceTolerance: 0.25);
var engine = new Cartesian3DEngine("position", domain, options);

var points = GeneratePoints();
var nearQueries = BuildNearQueries(engine);
var boxes = BuildBoundingBoxes();

if (nearQueries.All(query => !query.expectCapped))
{
    throw new InvalidOperationException("At least one near query must trigger the MaxCoveringCells fallback.");
}

var fixture = new
{
    name = "cartesian3d_lattice_dense",
    domain = new
    {
        min = new[] { -100, -100, -100 },
        max = new[] { 100, 100, 100 }
    },
    options = new
    {
        precisionBits = options.PrecisionBits,
        maxCoveringCells = options.MaxCoveringCells,
        distanceTolerance = options.DistanceTolerance,
        indexFieldName = options.IndexFieldName,
        boundingBoxFieldName = options.BoundingBoxFieldName
    },
    points = points.Select((point, index) => new { id = index, x = point.x, y = point.y, z = point.z }).ToArray(),
    nearQueries = nearQueries.Select(q => new { q.id, q.center, q.radius, q.expectCapped }).ToArray(),
    aabbQueries = boxes.Select(b => new { b.id, bounds = b.bounds }).ToArray()
};

var fixturePath = Path.Combine(LocateRepositoryRoot(), "LiteDB.Spatial.Core.Tests", "Differential", "Cartesian3D", "Fixtures", "cartesian3d_lattice_dense.json");
Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
var json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(fixturePath, json);
Console.WriteLine($"Fixture written to {fixturePath}");

static string LocateRepositoryRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
        var candidate = Path.Combine(current, "LiteDB.sln");
        if (File.Exists(candidate))
        {
            return current;
        }

        current = Path.GetDirectoryName(current);
    }

    throw new InvalidOperationException("Unable to locate repository root from execution directory.");
}

static IReadOnlyList<(double x, double y, double z)> GeneratePoints()
{
    var positions = new List<(double x, double y, double z)>();
    var coordinates = new[] { -60d, -40d, -20d, 0d, 20d, 40d, 60d };

    foreach (var x in coordinates)
    {
        foreach (var y in coordinates)
        {
            foreach (var z in coordinates)
            {
                positions.Add((x, y, z));
            }
        }
    }

    return positions;
}

static IReadOnlyList<(string id, double[] center, double radius, bool expectCapped)> BuildNearQueries(Cartesian3DEngine engine)
{
    var queries = new List<(string id, double[] center, double radius, bool expectCapped)>();

    var center = new GeoPoint3D(0, 0, 0);
    var cappedRadius = FindCappedRadius(engine, center, start: 2, step: 2, max: 80);
    queries.Add(("origin_dense", new[] { center.X, center.Y, center.Z }, cappedRadius.radius, cappedRadius.expectCapped));

    var offset = new GeoPoint3D(45, -45, 30);
    var offsetRadius = 18d;
    var offsetPlan = engine.PlanNear(offset, offsetRadius);
    queries.Add(("offset_cluster", new[] { offset.X, offset.Y, offset.Z }, offsetRadius, offsetPlan.Covering.WasCapped));

    return queries;
}

static (double radius, bool expectCapped) FindCappedRadius(Cartesian3DEngine engine, GeoPoint3D center, double start, double step, double max)
{
    var radius = start;
    while (radius <= max)
    {
        var plan = engine.PlanNear(center, radius);
        if (plan.Covering.WasCapped)
        {
            return (radius, true);
        }

        radius += step;
    }

    var fallbackPlan = engine.PlanNear(center, max);
    return (max, fallbackPlan.Covering.WasCapped);
}

static IReadOnlyList<(string id, double[] bounds)> BuildBoundingBoxes()
{
    return new List<(string id, double[] bounds)>
    {
        ("long_slab", new[] { -80d, -15d, -15d, 80d, 15d, 15d }),
        ("tall_column", new[] { -15d, -80d, -15d, 15d, 80d, 15d }),
        ("flat_slice", new[] { -30d, -30d, -5d, 30d, 30d, 5d })
    };
}
