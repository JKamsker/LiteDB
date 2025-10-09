using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public static class FixtureLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T Load<T>(string relativePath)
    {
        var path = ResolvePath(relativePath);
        var json = File.ReadAllText(path);
        var value = JsonSerializer.Deserialize<T>(json, Options);
        if (value == null)
        {
            throw new InvalidOperationException($"Fixture '{relativePath}' could not be deserialized.");
        }

        return value;
    }

    private static string ResolvePath(string relativePath)
    {
        var segments = relativePath
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        var components = new string[segments.Length + 1];
        components[0] = AppContext.BaseDirectory;
        Array.Copy(segments, 0, components, 1, segments.Length);
        return Path.Combine(components);
    }
}
