using System;
using System.IO;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class FailureSnapshot
{
    public static void Capture(string scenario, object payload)
    {
        try
        {
            var directory = TestResourceLocator.GetFailuresDirectory();
            var fileName = $"{Sanitize(scenario)}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}.json";
            var path = Path.Combine(directory, fileName);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to capture failure snapshot for '{scenario}': {ex}");
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }

        return value;
    }
}
