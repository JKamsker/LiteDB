using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public static class TestFixtureLoader
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<LocalityFixture> LoadLocalityFixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Indexing", "Fixtures", "locality-fixtures.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Locality fixture '{path}' was not copied to the output directory.");
        }

        var json = File.ReadAllText(path);
        var fixtures = JsonSerializer.Deserialize<List<LocalityFixture>>(json, Options);
        if (fixtures is null || fixtures.Count == 0)
        {
            throw new InvalidOperationException("Failed to load locality fixtures.");
        }

        return fixtures;
    }
}
