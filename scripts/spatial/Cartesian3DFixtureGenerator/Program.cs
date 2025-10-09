using System.Text.Json;
using System.Text.Json.Serialization;

var fixture = FixtureFactory.Create();
var root = RepositoryRootLocator.Find();
var outputDirectory = Path.Combine(root, "LiteDB.Spatial.Core.Tests", "Differential", "Cartesian3D", "Fixtures");
Directory.CreateDirectory(outputDirectory);
var outputPath = Path.Combine(outputDirectory, "cartesian3d-lattice.json");

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var json = JsonSerializer.Serialize(fixture, options);
File.WriteAllText(outputPath, json);

Console.WriteLine($"Wrote fixture to {outputPath}");

static class FixtureFactory
{
    public static LatticeFixture Create()
    {
        var domain = new Domain(-50, -50, -25, 50, 50, 25);
        var xs = new[] { -40, -20, 0, 20, 40 };
        var ys = new[] { -40, -10, 10, 30 };
        var zs = new[] { -24, -8, 8, 24 };

        var points = new List<LatticePoint>();
        var id = 1;

        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                foreach (var z in zs)
                {
                    points.Add(new LatticePoint(id++, x, y, z));
                }
            }
        }

        var nearQueries = new List<NearQuery>
        {
            new("origin-tight", new Point3D(0, 0, 0), 18, 1e-9),
            new("offset-diagonal", new Point3D(30, 20, 16), 25, 1e-9),
            new("wide-fallback", new Point3D(0, 0, 0), 70, 1e-9)
        };

        var boxQueries = new List<BoxQuery>
        {
            new("center-cube", new Point3D(-10, -10, -10), new Point3D(10, 10, 10)),
            new("slab-x-wide", new Point3D(-50, -5, -25), new Point3D(50, 5, 25)),
            new("upper-octant", new Point3D(0, 0, 0), new Point3D(50, 50, 25)),
            new("thin-y-band", new Point3D(-40, -10, -24), new Point3D(40, 10, 24))
        };

        return new LatticeFixture(domain, points, nearQueries, boxQueries, MaxCoveringCells: 4, DistanceTolerance: 1e-6);
    }
}

static class RepositoryRootLocator
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LiteDB.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root containing LiteDB.sln.");
    }
}

public sealed record LatticeFixture(
    Domain Domain,
    IReadOnlyList<LatticePoint> Points,
    IReadOnlyList<NearQuery> NearQueries,
    IReadOnlyList<BoxQuery> BoxQueries,
    int MaxCoveringCells,
    double DistanceTolerance);

public sealed record Domain(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

public sealed record LatticePoint(int Id, double X, double Y, double Z);

public sealed record NearQuery(string Name, Point3D Center, double Radius, double DistanceTolerance);

public sealed record BoxQuery(string Name, Point3D Min, Point3D Max);

public sealed record Point3D(double X, double Y, double Z);
