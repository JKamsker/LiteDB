using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial.Testing.Oracles;

internal static class OracleEnvironment
{
    private static readonly HashSet<string> EnabledOracles = BuildEnabledOracles();
    private static readonly bool DatabaseTestsEnabled = ParseBoolean(Environment.GetEnvironmentVariable("SPATIAL_DB_TESTS"));

    public static bool IsEnabled(string name, bool isDatabaseOracle, out string? skipReason)
    {
        if (isDatabaseOracle && !DatabaseTestsEnabled)
        {
            skipReason = "Database-backed oracles require SPATIAL_DB_TESTS=1";
            return false;
        }

        if (EnabledOracles.Count == 0)
        {
            skipReason = null;
            return true;
        }

        if (EnabledOracles.Contains("*"))
        {
            skipReason = null;
            return true;
        }

        if (EnabledOracles.Contains(name))
        {
            skipReason = null;
            return true;
        }

        skipReason = $"Oracle '{name}' not enabled in SPATIAL_ORACLES";
        return false;
    }

    private static HashSet<string> BuildEnabledOracles()
    {
        var raw = Environment.GetEnvironmentVariable("SPATIAL_ORACLES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var tokens = raw
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim());

        return new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ParseBoolean(string? value)
        => value != null && new[] { "1", "true", "yes", "on" }.Contains(value, StringComparer.OrdinalIgnoreCase);
}
