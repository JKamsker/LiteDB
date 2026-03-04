#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class FailureReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Record(string scenario, object payload)
    {
        try
        {
            var root = LocateRepositoryRoot();
            var directory = Path.Combine(root, "tests", "failures", "3d");
            Directory.CreateDirectory(directory);
            var fileName = Sanitize(scenario) + ".json";
            var path = Path.Combine(directory, fileName);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Failure reporting should never cause the test to fail.
        }
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LiteDB.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository root for failure reporting.");
    }

    private static string Sanitize(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
        {
            return "scenario";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var buffer = scenario
            .ToLowerInvariant()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();

        return new string(buffer);
    }
}
