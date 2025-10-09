using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

public static class FailureSnapshot
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write(string category, string fixtureId, object payload)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(fixtureId))
        {
            throw new ArgumentException("Fixture identifier is required.", nameof(fixtureId));
        }

        var failuresRoot = Path.Combine(FixtureLoader.FixturesRoot, "..", "failures");
        Directory.CreateDirectory(failuresRoot);

        var categoryFolder = Path.Combine(failuresRoot, category);
        Directory.CreateDirectory(categoryFolder);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var safeId = fixtureId.Replace(':', '_').Replace('/', '_');
        var fileName = $"{timestamp}-{safeId}.json";
        var path = Path.Combine(categoryFolder, fileName);

        var json = JsonSerializer.Serialize(payload, Options);
        File.WriteAllText(path, json);
    }
}
