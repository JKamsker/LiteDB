#nullable enable

using System;
using System.IO;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests;

internal static class FixtureLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Lazy<string> ProjectRoot = new(LocateProjectRoot, isThreadSafe: true);

    public static T Load<T>(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Fixture path must be provided.", nameof(relativePath));
        }

        var normalized = Normalize(relativePath);
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "fixtures", normalized);

        if (!File.Exists(runtimePath))
        {
            throw new FileNotFoundException($"Fixture '{relativePath}' not found in '{runtimePath}'.");
        }

        using var stream = File.OpenRead(runtimePath);
        var value = JsonSerializer.Deserialize<T>(stream, Options);

        if (value is null)
        {
            throw new InvalidOperationException($"Fixture '{relativePath}' could not be deserialized as {typeof(T).Name}.");
        }

        return value;
    }

    public static void Save<T>(string relativePath, T value)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Fixture path must be provided.", nameof(relativePath));
        }

        var normalized = Normalize(relativePath);
        var target = Path.Combine(ProjectRoot.Value, "fixtures", normalized);
        var directory = Path.GetDirectoryName(target);

        if (directory is null)
        {
            throw new InvalidOperationException($"Unable to determine directory for fixture '{relativePath}'.");
        }

        Directory.CreateDirectory(directory);

        using var stream = File.Create(target);
        JsonSerializer.Serialize(stream, value, Options);
    }

    private static string LocateProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "LiteDB.Spatial.Core.Tests.csproj");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the LiteDB.Spatial.Core.Tests project root.");
    }

    private static string Normalize(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar);
    }
}
