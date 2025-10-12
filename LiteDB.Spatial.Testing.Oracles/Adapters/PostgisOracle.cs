using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Spatial.Testing.Oracles.Infrastructure;
using Npgsql;

namespace LiteDB.Spatial.Testing.Oracles.Adapters;

/// <summary>
/// Minimal helper for issuing PostGIS queries during integration-style tests.
/// </summary>
public sealed class PostgisOracle : IAsyncDisposable
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgisOracle"/> class.
    /// </summary>
    /// <param name="connectionString">Optional connection string. When omitted, the <c>POSTGIS_CONNECTION_STRING</c> environment variable is used.</param>
    public PostgisOracle(string? connectionString = null)
    {
        _connectionString = connectionString ?? Environment.GetEnvironmentVariable("POSTGIS_CONNECTION_STRING")
            ?? throw new InvalidOperationException("POSTGIS_CONNECTION_STRING must be provided when SPATIAL_DB_TESTS is enabled.");
    }

    /// <summary>
    /// Gets a value indicating whether the PostGIS oracle can be used.
    /// </summary>
    public static bool IsAvailable => OracleEnvironment.AreDatabaseTestsEnabled && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POSTGIS_CONNECTION_STRING"));

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Executes the supplied SQL and returns the first column of the first row as a double.
    /// </summary>
    public async Task<double> ExecuteScalarAsync(string sql, CancellationToken cancellationToken = default)
    {
        OracleEnvironment.EnsureDatabaseTestsEnabled(nameof(PostgisOracle));
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (result == null || result is DBNull)
        {
            throw new InvalidOperationException("The query did not return a scalar result.");
        }

        return Convert.ToDouble(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Executes a SQL query and projects each row into a value using the supplied <paramref name="projector"/>.
    /// </summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> projector, CancellationToken cancellationToken = default)
    {
        OracleEnvironment.EnsureDatabaseTestsEnabled(nameof(PostgisOracle));
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(projector(reader));
        }

        return results;
    }
}
