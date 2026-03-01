using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LiteDB.Spatial.Testing.Oracles.Infrastructure;

/// <summary>
/// Provides environment-aware toggles for expensive oracle integrations.
/// </summary>
public static class OracleEnvironment
{
    private static readonly Lazy<IReadOnlyCollection<string>> AllowedOracles = new(ParseAllowedOracles);
    private static readonly Lazy<bool> DatabaseTestsEnabled = new(() => ReadBoolean("SPATIAL_DB_TESTS", defaultValue: false));
    private static readonly string[] DefaultOracleKeys = { "nts", "geographiclib", "mathnet", "postgis", "all" };

    /// <summary>
    /// Gets a value indicating whether external oracle integrations should be exercised.
    /// </summary>
    public static bool AreOraclesEnabled => AllowedOracles.Value.Count > 0;

    /// <summary>
    /// Gets a value indicating whether database-backed tests (e.g. PostGIS) are allowed to run.
    /// </summary>
    public static bool AreDatabaseTestsEnabled => DatabaseTestsEnabled.Value;

    /// <summary>
    /// Gets a value indicating whether the specified oracle key is permitted by SPATIAL_ORACLES.
    /// </summary>
    public static bool IsOracleEnabled(string oracleKey)
    {
        if (!AreOraclesEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(oracleKey))
        {
            return true;
        }

        var normalized = Normalize(oracleKey);
        var allowed = AllowedOracles.Value;
        if (allowed.Contains("all"))
        {
            return true;
        }

        return allowed.Contains(normalized);
    }

    /// <summary>
    /// Ensures that oracle integrations are enabled in the current environment.
    /// </summary>
    public static void EnsureOraclesEnabled(string oracleKey)
    {
        if (!AreOraclesEnabled)
        {
            throw new InvalidOperationException($"Oracle '{oracleKey}' was requested but SPATIAL_ORACLES is disabled.");
        }

        if (!IsOracleEnabled(oracleKey))
        {
            throw new InvalidOperationException($"Oracle '{oracleKey}' is disabled via SPATIAL_ORACLES.");
        }
    }

    /// <summary>
    /// Ensures that database-backed integrations are enabled in the current environment.
    /// </summary>
    public static void EnsureDatabaseTestsEnabled(string feature)
    {
        if (!AreDatabaseTestsEnabled)
        {
            throw new InvalidOperationException($"Database-backed oracle '{feature}' was requested but SPATIAL_DB_TESTS is disabled.");
        }
    }

    /// <summary>
    /// Returns a skip reason based on the configured environment variables.
    /// </summary>
    public static string? GetSkipReason(bool requiresDatabase, string? feature = null, params string[] requiredOracles)
    {
        if (!AreOraclesEnabled)
        {
            return $"Skipped because SPATIAL_ORACLES is disabled{FormatFeature(feature)}.";
        }

        if (requiresDatabase && !AreDatabaseTestsEnabled)
        {
            return $"Skipped because SPATIAL_DB_TESTS is disabled{FormatFeature(feature)}.";
        }

        if (requiredOracles.Length > 0)
        {
            foreach (var oracle in requiredOracles.Select(Normalize).Where(o => o.Length > 0))
            {
                if (!IsOracleEnabled(oracle))
                {
                    return $"Skipped because spatial oracle '{oracle}' is disabled{FormatFeature(feature)}.";
                }
            }
        }

        return null;
    }

    private static string FormatFeature(string? feature)
    {
        return string.IsNullOrWhiteSpace(feature) ? string.Empty : $" for '{feature}'";
    }

    private static IReadOnlyCollection<string> ParseAllowedOracles()
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var value = Environment.GetEnvironmentVariable("SPATIAL_ORACLES");
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(DefaultOracleKeys, comparer);
        }

        value = value.Trim();
        if (IsDisabled(value))
        {
            return Array.Empty<string>();
        }

        if (IsEnabled(value))
        {
            return new HashSet<string>(DefaultOracleKeys, comparer);
        }

        var tokens = value
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .Distinct(comparer)
            .ToArray();

        return tokens.Length == 0 ? Array.Empty<string>() : new HashSet<string>(tokens, comparer);
    }

    private static bool IsDisabled(string value)
    {
        return value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase)
            || value.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnabled(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("enabled", StringComparison.OrdinalIgnoreCase)
            || value.Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static bool ReadBoolean(string variable, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        value = value.Trim();
        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer != 0;
        }

        return !value.Equals("off", StringComparison.OrdinalIgnoreCase) && !value.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }
}
