using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial.Testing.Oracles;

internal static class OracleEnvironment
{
    private static readonly HashSet<string> _allowed = Parse(Environment.GetEnvironmentVariable("SPATIAL_ORACLES"));
    private static readonly bool _dbEnabled = ParseBoolean(Environment.GetEnvironmentVariable("SPATIAL_DB_TESTS"));

    public static bool Allows(string oracleKey)
    {
        if (_allowed.Count == 0)
        {
            return false;
        }

        return _allowed.Contains("all") || _allowed.Contains(oracleKey);
    }

    public static bool DatabaseEnabled => _dbEnabled;

    private static HashSet<string> Parse(string? value)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(new[] { "nts", "geographiclib", "mathnet", "postgis", "all" }, comparer);
        }

        value = value.Trim();
        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(comparer);
        }

        var tokens = value
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.ToLowerInvariant());

        return new HashSet<string>(tokens, comparer);
    }

    private static bool ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
