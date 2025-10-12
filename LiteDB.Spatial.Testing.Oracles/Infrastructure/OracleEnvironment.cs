using System.Globalization;

namespace LiteDB.Spatial.Testing.Oracles.Infrastructure;

/// <summary>
/// Provides environment-aware toggles for expensive oracle integrations.
/// </summary>
public static class OracleEnvironment
{
    private static readonly Lazy<bool> OraclesEnabled = new(() => ReadBoolean("SPATIAL_ORACLES", defaultValue: true));
    private static readonly Lazy<bool> DatabaseTestsEnabled = new(() => ReadBoolean("SPATIAL_DB_TESTS", defaultValue: false));

    /// <summary>
    /// Gets a value indicating whether external oracle integrations should be exercised.
    /// </summary>
    public static bool AreOraclesEnabled => OraclesEnabled.Value;

    /// <summary>
    /// Gets a value indicating whether database-backed tests (e.g. PostGIS) are allowed to run.
    /// </summary>
    public static bool AreDatabaseTestsEnabled => DatabaseTestsEnabled.Value;

    /// <summary>
    /// Ensures that oracle integrations are enabled in the current environment.
    /// </summary>
    public static void EnsureOraclesEnabled(string feature)
    {
        if (!AreOraclesEnabled)
        {
            throw new InvalidOperationException($"Oracle feature '{feature}' was requested but SPATIAL_ORACLES is disabled.");
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
    public static string? GetSkipReason(bool requiresDatabase, string? feature = null)
    {
        if (!AreOraclesEnabled)
        {
            return $"Skipped because SPATIAL_ORACLES is disabled{FormatFeature(feature)}.";
        }

        if (requiresDatabase && !AreDatabaseTestsEnabled)
        {
            return $"Skipped because SPATIAL_DB_TESTS is disabled{FormatFeature(feature)}.";
        }

        return null;
    }

    private static string FormatFeature(string? feature)
    {
        return string.IsNullOrWhiteSpace(feature) ? string.Empty : $" for '{feature}'";
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
