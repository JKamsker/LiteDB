using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

/// <summary>
/// Writes snapshots of failing oracle comparisons to disk for later analysis.
/// </summary>
internal static class FixtureSnapshot
{
    private static readonly Lazy<string> SnapshotRoot = new(() => EnsureDirectory(Path.Combine(FixtureLoader.RepositoryRootPath, "tests", "failures")));
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void RecordDistanceFailure(string fixtureId, double expected, double actual, double tolerance)
    {
        var payload = new
        {
            FixtureId = fixtureId,
            Expected = expected,
            Actual = actual,
            Tolerance = tolerance,
            Delta = Math.Abs(actual - expected),
            CapturedUtc = DateTime.UtcNow
        };

        Record(fixtureId, payload, "distance");
    }

    public static void Record<T>(string fixtureId, T payload, string category)
    {
        var fileName = $"{Sanitize(fixtureId)}-{category}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
        var fullPath = Path.Combine(SnapshotRoot.Value, fileName);
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        File.WriteAllText(fullPath, json);
    }

    private static string EnsureDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
