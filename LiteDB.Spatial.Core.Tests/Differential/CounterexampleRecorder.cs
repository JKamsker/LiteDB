#nullable enable

using System;
using System.IO;
using System.Text.Json;
using LiteDB.Spatial.Core.Tests.TestSupport;

namespace LiteDB.Spatial.Core.Tests.Differential;

/// <summary>
/// Persists failing property test inputs to <c>/tests/failures</c> for later triage.
/// </summary>
public static class CounterexampleRecorder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Record<T>(string scenario, T payload, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(scenario))
        {
            throw new ArgumentException("Scenario name must be provided.", nameof(scenario));
        }

        var directory = RepositoryPath.Combine("tests", "failures");
        Directory.CreateDirectory(directory);

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{scenario}.json";
        var path = Path.Combine(directory, fileName);

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("scenario", scenario);
        writer.WriteString("timestamp", DateTimeOffset.UtcNow);
        writer.WriteString("exception", exception.ToString());
        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, payload, Options);
        writer.WriteEndObject();
    }
}
