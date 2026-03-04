using System.Text.Json;
using System.Text.Json.Serialization;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var fixturesDir = Path.Combine(repoRoot, "LiteDB.Spatial.Core.Tests", "Differential", "Cartesian3D", "Fixtures");
Directory.CreateDirectory(fixturesDir);

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var fixtures = new[]
{
    GenerateLatticeFixture()
};

foreach (var fixture in fixtures)
{
    var path = Path.Combine(fixturesDir, $"{fixture.Name}.json");
    var json = JsonSerializer.Serialize(fixture, jsonOptions);
    File.WriteAllText(path, json);
    Console.WriteLine($"Wrote {path}");
}

return;

static Cartesian3DLatticeFixture GenerateLatticeFixture()
{
    const string name = "lattice-aspects";
    var domain = new Domain(new[] { -12d, -12d, -12d }, new[] { 12d, 12d, 12d });
    var options = new FixtureOptions(12, 4, 0.05);
    var points = new List<LatticePoint>();
    var coordinates = new[] { -12d, -6d, 0d, 6d, 12d };
    var index = 0;

    foreach (var x in coordinates)
    {
        foreach (var y in coordinates)
        {
            foreach (var z in coordinates)
            {
                points.Add(new LatticePoint($"P{index:000}", new[] { x, y, z }));
                index++;
            }
        }
    }

    var queries = new List<LatticeQuery>
    {
        new("TightOrigin", new[] { 0d, 0d, 0d }, 1.5, 0.05, true),
        new("OriginShell", new[] { 0d, 0d, 0d }, 4.5, 0.05, true),
        new("EdgeSweep", new[] { 9d, 9d, 9d }, 6.5, 0.05, true)
    };

    var boxes = new List<LatticeBoundingBox>
    {
        new("CoreCube", new[] { -6d, -6d, -6d }, new[] { 6d, 6d, 6d }, true),
        new("NeedleX", new[] { -12d, -1d, -1d }, new[] { 12d, 1d, 1d }, true),
        new("OffsetSlab", new[] { -12d, -12d, 6d }, new[] { -4d, -4d, 12d }, true)
    };

    return new Cartesian3DLatticeFixture(name, domain, options, points, queries, boxes);
}

internal sealed record Cartesian3DLatticeFixture(
    string Name,
    Domain Domain,
    FixtureOptions Options,
    IReadOnlyList<LatticePoint> Points,
    IReadOnlyList<LatticeQuery> Queries,
    IReadOnlyList<LatticeBoundingBox> Boxes);

internal sealed record Domain(double[] Min, double[] Max);

internal sealed record FixtureOptions(int PrecisionBits, int MaxCoveringCells, double DistanceTolerance);

internal sealed record LatticePoint(string Id, double[] Coordinates);

internal sealed record LatticeQuery(string Name, double[] Center, double Radius, double Tolerance, bool ExpectFallback);

internal sealed record LatticeBoundingBox(string Name, double[] Min, double[] Max, bool ExpectFallback);
