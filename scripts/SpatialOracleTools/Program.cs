using System.Text.Json;
using System.Text.Json.Serialization;
using Geodesy;

var serializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip
};

var command = args.Length == 0 ? "geodesic" : args[0].TrimStart('-').ToLowerInvariant();

switch (command)
{
    case "geodesic":
        RefreshGeodesicPairs(serializerOptions);
        break;
    case "compare":
        CompareSamples(serializerOptions);
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Supported commands: geodesic");
        Environment.Exit(1);
        break;
}

static void RefreshGeodesicPairs(JsonSerializerOptions serializerOptions)
{
    var repoRoot = FindRepoRoot();
    var fixturesPath = Path.Combine(repoRoot, "LiteDB.Spatial.Core.Tests", "fixtures");
    var source = Path.Combine(fixturesPath, "geodesic_pairs.json");
    if (!File.Exists(source))
    {
        throw new FileNotFoundException("Could not locate geodesic_pairs.json fixture.", source);
    }

    var json = File.ReadAllText(source);
    var pairs = JsonSerializer.Deserialize<List<GeodesicPair>>(json, serializerOptions);
    if (pairs == null)
    {
        throw new InvalidOperationException("Unable to deserialize geodesic pair fixtures.");
    }

    var calculator = new GeodeticCalculator(Ellipsoid.WGS84);
    var results = pairs.Select(pair => new GeodesicResult
    {
        Id = pair.Id,
        Lon1 = pair.Lon1,
        Lat1 = pair.Lat1,
        Lon2 = pair.Lon2,
        Lat2 = pair.Lat2,
        DistanceMeters = calculator.CalculateGeodeticCurve(
            new GlobalCoordinates(new Angle(pair.Lat1), new Angle(pair.Lon1)),
            new GlobalCoordinates(new Angle(pair.Lat2), new Angle(pair.Lon2))).EllipsoidalDistance
    }).ToList();

    var destination = Path.Combine(fixturesPath, "geodesic_pairs.oracle.json");
    var output = JsonSerializer.Serialize(results, serializerOptions);
    File.WriteAllText(destination, output);

    Console.WriteLine($"Wrote {results.Count} geodesic distances to {destination}.");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null && !File.Exists(Path.Combine(current.FullName, "LiteDB.sln")))
    {
        current = current.Parent;
    }

    if (current == null)
    {
        throw new InvalidOperationException("Could not locate repository root (LiteDB.sln).");
    }

    return current.FullName;
}

static void CompareSamples(JsonSerializerOptions serializerOptions)
{
    var repoRoot = FindRepoRoot();
    var fixturesPath = Path.Combine(repoRoot, "LiteDB.Spatial.Core.Tests", "fixtures");
    var source = Path.Combine(fixturesPath, "geodesic_pairs.json");
    var json = File.ReadAllText(source);
    var pairs = JsonSerializer.Deserialize<List<GeodesicPair>>(json, serializerOptions) ?? new();
    var calculator = new GeodeticCalculator(Ellipsoid.WGS84);

    foreach (var pair in pairs)
    {
        var geodesy = calculator.CalculateGeodeticCurve(
            new GlobalCoordinates(new Angle(pair.Lat1), new Angle(pair.Lon1)),
            new GlobalCoordinates(new Angle(pair.Lat2), new Angle(pair.Lon2))).EllipsoidalDistance;
        var haversine = Haversine(pair.Lat1, pair.Lon1, pair.Lat2, pair.Lon2);

        Console.WriteLine($"{pair.Id}: geodesy={geodesy:N3}, haversine={haversine:N3}, delta={(geodesy - haversine):N3}");
    }
}

static double Haversine(double lat1, double lon1, double lat2, double lon2)
{
    const double radius = 6378137d;
    static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    var phi1 = ToRadians(lat1);
    var phi2 = ToRadians(lat2);
    var deltaPhi = ToRadians(lat2 - lat1);
    var deltaLambda = ToRadians(lon2 - lon1);

    var sinPhi = Math.Sin(deltaPhi / 2d);
    var sinLambda = Math.Sin(deltaLambda / 2d);
    var a = sinPhi * sinPhi + Math.Cos(phi1) * Math.Cos(phi2) * sinLambda * sinLambda;
    var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
    return radius * c;
}

internal class GeodesicPair
{
    public string Id { get; set; } = string.Empty;
    public double Lon1 { get; set; }
    public double Lat1 { get; set; }
    public double Lon2 { get; set; }
    public double Lat2 { get; set; }
}

internal class GeodesicResult : GeodesicPair
{
    public double DistanceMeters { get; set; }
}
