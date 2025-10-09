using System;
using System.Collections.Generic;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using LiteDB.Spatial.Testing.Oracles.Oracles;

namespace LiteDB.Spatial.Testing.Oracles;

public static class OracleRegistry
{
    private static readonly Dictionary<string, Func<IOracleMetadata>> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nts"] = () => new NtsOracle(),
        ["geographiclib"] = () => new GeographicLibOracle(),
        ["mathnet"] = () => new MathNetOracle3D(),
        ["postgis"] = () => new PostgisOracle(),
    };

    private static readonly HashSet<string> DatabaseBacked = new(StringComparer.OrdinalIgnoreCase)
    {
        "postgis",
    };

    public static bool TryCreate<T>(string name, out T? oracle)
        where T : class, IOracleMetadata
    {
        if (!Factories.TryGetValue(name, out var factory))
        {
            oracle = null;
            return false;
        }

        var created = factory();
        if (created is T typed && created.IsAvailable)
        {
            oracle = typed;
            return true;
        }

        oracle = null;
        return false;
    }

    public static IReadOnlyCollection<IOracleMetadata> CreateDefaults()
    {
        var list = new List<IOracleMetadata>();
        foreach (var factory in Factories.Values)
        {
            list.Add(factory());
        }

        return list;
    }

    internal static bool IsDatabaseOracle(string name)
        => DatabaseBacked.Contains(name);
}
