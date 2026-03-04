#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using LiteDB.Spatial;
using Npgsql;

namespace LiteDB.Spatial.Core.Tests.Oracles;

internal sealed class PostgisOracle : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    private PostgisOracle(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public static bool TryCreate(out PostgisOracle? oracle, out string? skipReason)
    {
        var toggle = Environment.GetEnvironmentVariable("SPATIAL_DB_TESTS");

        if (string.IsNullOrWhiteSpace(toggle))
        {
            oracle = null;
            skipReason = "SPATIAL_DB_TESTS not set.";
            return false;
        }

        var connectionString = toggle.Contains('=')
            ? toggle
            : Environment.GetEnvironmentVariable("POSTGIS_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
        }

        try
        {
            var dataSource = NpgsqlDataSource.Create(connectionString);
            using var connection = dataSource.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "select PostGIS_Full_Version()";
            command.CommandType = CommandType.Text;
            command.ExecuteScalar();
            oracle = new PostgisOracle(dataSource);
            skipReason = null;
            return true;
        }
        catch (Exception ex)
        {
            oracle = null;
            skipReason = $"Unable to connect to PostGIS: {ex.Message}";
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> QueryDWithinAsync(
        IReadOnlyList<PostgisPoint> points,
        GeoPoint center,
        double radiusMeters)
    {
        var tableName = "temp_points_" + Guid.NewGuid().ToString("N");

        await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = $"create temp table {tableName} (id text primary key, geom geography(Point,4326)) on commit drop";
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var point in points)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"insert into {tableName} (id, geom) values (@id, ST_SetSRID(ST_MakePoint(@lon, @lat),4326)::geography)";
            insert.Parameters.AddWithValue("id", point.Id);
            insert.Parameters.AddWithValue("lon", point.Longitude);
            insert.Parameters.AddWithValue("lat", point.Latitude);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        await using var query = connection.CreateCommand();
        query.CommandText = $"select id from {tableName} where ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@lon, @lat),4326)::geography, @radius)";
        query.Parameters.AddWithValue("lon", center.Longitude);
        query.Parameters.AddWithValue("lat", center.Latitude);
        query.Parameters.AddWithValue("radius", radiusMeters);

        var results = new List<string>();

        await using var reader = await query.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    internal sealed class PostgisPoint
    {
        public string Id { get; set; } = string.Empty;

        public double Longitude { get; set; }

        public double Latitude { get; set; }
    }
}
