using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB.Spatial.Core.Tests.TestSupport;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

internal static class DifferentialFailureRecorder
{
    private static readonly string FailureDirectory = Path.Combine(TestResourceLocator.GetFailuresDirectory(), "3d");

    public static void Record(string fixtureName, string queryId, object payload)
    {
        Directory.CreateDirectory(FailureDirectory);
        var fileName = $"{Sanitize(fixtureName)}_{Sanitize(queryId)}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
        var path = Path.Combine(FailureDirectory, fileName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var json = JsonSerializer.Serialize(payload, options);
        File.WriteAllText(path, json);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}
