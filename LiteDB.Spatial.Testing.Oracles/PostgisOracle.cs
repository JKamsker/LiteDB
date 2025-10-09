using Npgsql;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Provides helper methods to interact with a PostGIS instance for differential testing.
/// </summary>
public sealed class PostgisOracle
{
    private readonly string _connectionString;

    public PostgisOracle(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task EnsureExtensionAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS postgis;", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SeedPointsAsync(string tableName, IReadOnlyList<(int Id, double Lon, double Lat)> points)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS {tableName};", connection))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = new NpgsqlCommand($"CREATE TABLE {tableName} (id INTEGER PRIMARY KEY, geom GEOGRAPHY(Point, 4326));", connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        foreach (var point in points)
        {
            await using var insert = new NpgsqlCommand($"INSERT INTO {tableName} (id, geom) VALUES (@id, ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography);", connection);
            insert.Parameters.AddWithValue("id", point.Id);
            insert.Parameters.AddWithValue("lon", point.Lon);
            insert.Parameters.AddWithValue("lat", point.Lat);
            await insert.ExecuteNonQueryAsync();
        }
    }

    public async Task<IReadOnlyList<int>> QueryDWithinAsync(string tableName, double lon, double lat, double radiusMeters)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand($"SELECT id FROM {tableName} WHERE ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography, @radius);", connection);
        command.Parameters.AddWithValue("lon", lon);
        command.Parameters.AddWithValue("lat", lat);
        command.Parameters.AddWithValue("radius", radiusMeters);

        var results = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetInt32(0));
        }

        return results;
    }
}
