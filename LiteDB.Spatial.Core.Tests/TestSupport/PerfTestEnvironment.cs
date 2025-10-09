#nullable enable

using System;
using System.IO;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class PerfTestEnvironment
{
    public const string EnableEnv = "LITEDB_SPATIAL_PERF";
    public const string SqliteEnv = "LITEDB_SPATIAL_PERF_SQLITE";
    public const string PostgresEnv = "LITEDB_SPATIAL_PERF_POSTGIS";

    public static bool IsEnabled => string.Equals(Environment.GetEnvironmentVariable(EnableEnv), "1", StringComparison.OrdinalIgnoreCase);

    public static string SkipMessage => $"Set {EnableEnv}=1 to enable the spatial performance harness.";

    public static bool TryGetSqliteConnection(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable(SqliteEnv) ?? string.Empty;
        connectionString = connectionString.Trim();
        return connectionString.Length > 0;
    }

    public static bool TryGetPostgresConnection(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable(PostgresEnv) ?? string.Empty;
        connectionString = connectionString.Trim();
        return connectionString.Length > 0;
    }

    public static string GetBenchmarksDocumentPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        return Path.Combine(root, "docs", "benchmarks.md");
    }
}
