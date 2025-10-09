#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

public static class FixtureLoader
{
    private static readonly Lazy<string> _fixturesRoot = new(FindFixtureRoot);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string FixturesRoot => _fixturesRoot.Value;

    public static IReadOnlyList<GeodesicPair> LoadGeodesicPairs()
    {
        var path = ResolvePath("geodesic_pairs.json");
        using var stream = File.OpenRead(path);
        var pairs = JsonSerializer.Deserialize<List<GeodesicPair>>(stream, _jsonOptions);
        if (pairs is null)
        {
            throw new InvalidOperationException($"Failed to deserialize geodesic pairs from '{path}'.");
        }

        return pairs;
    }

    public static IReadOnlyList<GeoJsonPolygonFixture> LoadGeoJsonPolygons()
    {
        var directory = ResolvePath("geojson_polygons");
        return Directory
            .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new GeoJsonPolygonFixture(ReadId(path), ReadDescription(path), File.ReadAllText(path)))
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static PointCloud2D LoadPointCloud2D(string fileName)
    {
        var path = ResolvePath(Path.Combine("point_clouds", fileName));
        using var stream = File.OpenRead(path);
        var cloud = JsonSerializer.Deserialize<PointCloud2D>(stream, _jsonOptions);
        if (cloud is null)
        {
            throw new InvalidOperationException($"Failed to load point cloud fixture '{path}'.");
        }

        return cloud with { Description = cloud.Description ?? fileName };
    }

    public static PointCloud3D LoadPointCloud3D(string fileName)
    {
        var path = ResolvePath(Path.Combine("point_clouds", fileName));
        using var stream = File.OpenRead(path);
        var cloud = JsonSerializer.Deserialize<PointCloud3D>(stream, _jsonOptions);
        if (cloud is null)
        {
            throw new InvalidOperationException($"Failed to load point cloud fixture '{path}'.");
        }

        return cloud with { Description = cloud.Description ?? fileName };
    }

    private static string ResolvePath(string relative)
    {
        var path = Path.Combine(FixturesRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{relative}' was not found under '{FixturesRoot}'.", path);
        }

        return path;
    }

    private static string FindFixtureRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to find the '/tests/fixtures' directory from the current test base path.");
    }

    private static string ReadId(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            return idElement.GetString() ?? Path.GetFileNameWithoutExtension(path);
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static string? ReadDescription(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("description", out var description)
            && description.ValueKind == JsonValueKind.String)
        {
            return description.GetString();
        }

        return null;
    }
}
