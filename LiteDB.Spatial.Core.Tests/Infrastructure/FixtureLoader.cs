using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

/// <summary>
/// Provides strongly-typed access to the shared fixture packs under <c>tests/fixtures</c>.
/// </summary>
internal static class FixtureLoader
{
    private static readonly Lazy<string> RepositoryRoot = new(ResolveRepositoryRoot);
    private static readonly Lazy<string> FixtureRoot = new(() => Path.Combine(RepositoryRoot.Value, "tests", "fixtures"));

    public static string RepositoryRootPath => RepositoryRoot.Value;

    public static string FixtureRootPath => FixtureRoot.Value;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = false
    };

    public static string GetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(FixtureRoot.Value, normalized);
    }

    public static string LoadText(string relativePath)
    {
        var path = GetPath(relativePath);
        return File.ReadAllText(path);
    }

    public static T Load<T>(string relativePath)
    {
        var path = GetPath(relativePath);
        var json = File.ReadAllText(path);
        var value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
        if (value == null)
        {
            throw new InvalidOperationException($"Fixture '{relativePath}' could not be deserialized as {typeof(T).Name}.");
        }

        return value;
    }

    public static IReadOnlyList<T> LoadCollection<T>(string relativePath)
    {
        var result = Load<List<T>>(relativePath);
        return result;
    }

    private static string ResolveRepositoryRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LiteDB.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Unable to locate LiteDB.sln from '{baseDirectory}'.");
    }
}
