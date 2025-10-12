using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB.Spatial;
using Npgsql;

namespace LiteDB.Spatial.Core.Tests.Engine.Differential;

internal sealed class PostgisOracle : IAsyncDisposable
{
    private readonly string _tableName;
    private readonly NpgsqlConnection _connection;

    private PostgisOracle(NpgsqlConnection connection, string tableName)
    {
        _connection = connection;
        _tableName = tableName;
    }

    public static bool TryCreate(out PostgisOracle? oracle)
    {
        var connectionString = Environment.GetEnvironmentVariable("SPATIAL_DB_TESTS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            oracle = null;
            return false;
        }

        var tableName = "tmp_litedb_points_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnection(connectionString);
        oracle = new PostgisOracle(connection, tableName);
        return true;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync().ConfigureAwait(false);

        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "CREATE TEMP TABLE IF NOT EXISTS spatial_temp_guard(flag int);";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"CREATE TEMP TABLE {_tableName} (id TEXT PRIMARY KEY, geom geometry(Point, 4326));";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public async Task LoadPointsAsync(IEnumerable<GeographicFixturePoint> points)
    {
        var payload = points.ToArray();
        if (payload.Length == 0)
        {
            return;
        }

        await using var transaction = await _connection.BeginTransactionAsync().ConfigureAwait(false);

        foreach (var point in payload)
        {
            await using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {_tableName} (id, geom) VALUES (@id, ST_SetSRID(ST_MakePoint(@lon, @lat), 4326));";
            insert.Parameters.AddWithValue("@id", point.Id);
            insert.Parameters.AddWithValue("@lon", point.Location.Longitude);
            insert.Parameters.AddWithValue("@lat", point.Location.Latitude);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> QueryDWithinAsync(GeoPoint center, double radiusMeters)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $@"SELECT id
FROM {_tableName}
WHERE ST_DWithin(
    geom::geography,
    ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography,
    @radius)
ORDER BY id;";
        command.Parameters.AddWithValue("@lon", center.Longitude);
        command.Parameters.AddWithValue("@lat", center.Latitude);
        command.Parameters.AddWithValue("@radius", radiusMeters);

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.State == System.Data.ConnectionState.Open)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = $"TRUNCATE {_tableName};";
            try
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore clean-up failures, temporary tables are scoped to the session.
            }
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
