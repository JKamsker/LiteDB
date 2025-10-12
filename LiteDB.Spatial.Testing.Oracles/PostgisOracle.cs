using System;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Optional adapter for integration tests backed by a PostGIS database.
/// </summary>
public sealed class PostgisOracle
{
    public PostgisOracle(string? connectionString = null)
    {
        ConnectionString = connectionString ?? Environment.GetEnvironmentVariable("SPATIAL_POSTGIS_CONNECTION");
    }

    public string Name => "PostGIS";

    public string? ConnectionString { get; }

    public bool IsEnabled => OracleEnvironment.DatabaseEnabled && OracleEnvironment.Allows("postgis") && !string.IsNullOrWhiteSpace(ConnectionString);

    public void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("PostGIS oracle requires SPATIAL_DB_TESTS=1, SPATIAL_ORACLES to include 'postgis', and a connection string.");
        }
    }
}
