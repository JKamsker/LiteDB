using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

internal static class TestDataLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<T> LoadCollection<T>(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Fixture file name must be provided.", nameof(fileName));
        }

        var basePath = AppContext.BaseDirectory;
        var fullPath = Path.Combine(basePath, "fixtures", fileName);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Unable to locate fixture '{fileName}'. Expected at '{fullPath}'.", fullPath);
        }

        using var stream = File.OpenRead(fullPath);
        var payload = JsonSerializer.Deserialize<IReadOnlyList<T>>(stream, SerializerOptions);

        if (payload == null)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' could not be deserialized as {typeof(T).Name}.");
        }

        return payload;
    }
}
