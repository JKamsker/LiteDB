using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

var xs = new[] { -1.2, -0.4, 0.15, 0.85, 1.35 };
var ys = new[] { -0.9, -0.25, 0.3, 0.95 };
var zs = new[] { -1.1, -0.6, -0.05, 0.45, 1.05 };

var points = new List<object>();
var index = 0;
foreach (var x in xs)
{
    foreach (var y in ys)
    {
        foreach (var z in zs)
        {
            points.Add(new
            {
                id = $"P{index:000}",
                x = Math.Round(x, 5),
                y = Math.Round(y, 5),
                z = Math.Round(z, 5)
            });
            index++;
        }
    }
}

var fixture = new
{
    id = "cartesian3d-lattice-v1",
    domain = new
    {
        min = new { x = -1.5, y = -1.0, z = -1.3 },
        max = new { x = 1.5, y = 1.1, z = 1.2 }
    },
    points,
    nearQueries = new object[]
    {
        new { id = "origin-tight", center = new { x = 0.0, y = 0.0, z = 0.0 }, radius = 0.75 },
        new { id = "offset-slab", center = new { x = 0.9, y = -0.2, z = 0.5 }, radius = 0.6 },
        new { id = "wide-budget", center = new { x = 0.2, y = 0.55, z = -0.4 }, radius = 1.25 }
    },
    boundingBoxes = new object[]
    {
        new { id = "center-cube", min = new { x = -0.5, y = -0.5, z = -0.5 }, max = new { x = 0.5, y = 0.5, z = 0.5 } },
        new { id = "needle-x", min = new { x = -1.2, y = -0.3, z = -0.2 }, max = new { x = 1.35, y = 0.35, z = -0.05 } },
        new { id = "slab-z", min = new { x = -0.6, y = -0.9, z = -1.1 }, max = new { x = 0.9, y = 0.95, z = 0.0 } }
    }
};

var repoRoot = GetRepositoryRoot();
var targetDirectory = Path.Combine(repoRoot, "tests", "fixtures", "cartesian3d");
Directory.CreateDirectory(targetDirectory);
var targetPath = Path.Combine(targetDirectory, "lattice_v1.json");
var json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(targetPath, json);

Console.WriteLine($"Wrote {targetPath}");

static string GetRepositoryRoot()
{
    var current = AppContext.BaseDirectory;
    return Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", ".."));
}
