using System;
using System.IO;
using System.Linq;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

internal static class FixturePaths
{
    private static readonly Lazy<string> RepositoryRoot = new(ResolveRepositoryRoot);

    public static string FixturesRoot => Path.Combine(RepositoryRoot.Value, "tests", "fixtures");

    public static string GeodesicPairs => Path.Combine(FixturesRoot, "geodesic_pairs.json");

    public static string GeoJsonPolygons => Path.Combine(FixturesRoot, "geojson_polygons");

    public static string PointClouds => Path.Combine(FixturesRoot, "point_clouds");

    public static string Snapshots => Path.Combine(FixturesRoot, "_snapshots");

    private static string ResolveRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "LiteDB.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }

    public static string EnsureSnapshotsDirectory()
    {
        var path = Snapshots;
        Directory.CreateDirectory(path);
        return path;
    }

    public static string MakeSnapshotPath(string fixtureId, string extension)
    {
        var fileSafe = new string(fixtureId.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return Path.Combine(EnsureSnapshotsDirectory(), $"{fileSafe}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.{extension.TrimStart('.')}");
    }
}
