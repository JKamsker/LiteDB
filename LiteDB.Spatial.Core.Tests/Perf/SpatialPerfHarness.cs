using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests.Support;
using LiteDB.Spatial;
using Microsoft.Data.Sqlite;
using Npgsql;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Perf;

public sealed class SpatialPerfHarness
{
    private const string CaptureEnv = "LITEDB_SPATIAL_PERF_CAPTURE";
    private const string SqliteEnv = "LITEDB_SPATIAL_PERF_SQLITE";
    private const string PostgresEnv = "LITEDB_SPATIAL_PERF_POSTGRES";
    private const string OutputEnv = "LITEDB_SPATIAL_PERF_OUTPUT";

    [Fact]
    [Trait("Category", "perf")]
    public async Task CaptureBenchmarksAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(CaptureEnv), "1", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Skipping perf harness because {CaptureEnv} is not set to 1.");
            return;
        }

        var fixture = LocalityFixture.Load("uniform_2d_precision8");
        var metrics = fixture.Evaluate();
        metrics.ShouldMatchFixture();

        var summary = new PerfSummary(fixture, metrics);
        summary.AddResult(await BenchmarkLiteDbAsync(fixture, metrics));

        if (TryGetEnv(SqliteEnv, out var sqliteConnection))
        {
            summary.AddResult(await RunWithHandling(() => BenchmarkSqliteAsync(fixture, sqliteConnection), "SQLite RTree"));
        }
        else
        {
            summary.AddSkipped("SQLite RTree", $"Set {SqliteEnv} to a connection string (for example 'Data Source=spatial-perf.sqlite').");
        }

        if (TryGetEnv(PostgresEnv, out var postgresConnection))
        {
            summary.AddResult(await RunWithHandling(() => BenchmarkPostgresAsync(fixture, postgresConnection), "PostGIS"));
        }
        else
        {
            summary.AddSkipped("PostGIS", $"Set {PostgresEnv} to a PostgreSQL connection string (for example 'Host=localhost;Username=postgres;Password=postgres;Database=spatial').");
        }

        var outputPath = ResolveOutputPath();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, summary.ToMarkdown());
    }

    private static async Task<PerfResult> RunWithHandling(Func<Task<PerfResult>> benchmark, string engineName)
    {
        try
        {
            return await benchmark().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return PerfResult.CreateSkipped(engineName, $"Failed: {ex.Message}");
        }
    }

    private static Task<PerfResult> BenchmarkLiteDbAsync(LocalityFixture fixture, LocalityMetricsResult metrics)
    {
        var options = new SpatialIndexOptions(fixture.PrecisionBits, maxCoveringCells: 256);
        var domain = BoundingBox.From2D(0, 0, 1, 1);
        var engine = new Cartesian2DEngine("location", domain, options);
        var center = new GeoPoint(0.5, 0.5);
        var radius = 0.125;

        var stopwatch = Stopwatch.StartNew();
        var plan = engine.PlanNear(center, radius);
        var candidateCount = CountCandidates(metrics.ComputedCodes, plan.IndexRanges);
        var matches = CountMatches(fixture, engine, center, radius);
        stopwatch.Stop();

        var notes = $"ranges={plan.IndexRanges.Count}, theoretical={plan.IndexRanges.Sum(r => (long)(r.End - r.Start + 1))}, overlap={metrics.AverageOverlap:P1}";
        return Task.FromResult(new PerfResult("LiteDB (Cartesian2D)", stopwatch.Elapsed, candidateCount, matches, notes));
    }

    private static async Task<PerfResult> BenchmarkSqliteAsync(LocalityFixture fixture, string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, "DROP TABLE IF EXISTS spatial_perf_data;").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "DROP TABLE IF EXISTS spatial_perf_points;").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "CREATE TABLE spatial_perf_data(id INTEGER PRIMARY KEY, x REAL NOT NULL, y REAL NOT NULL);").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "CREATE VIRTUAL TABLE spatial_perf_points USING rtree(id, minX, maxX, minY, maxY);").ConfigureAwait(false);

        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using var insertData = connection.CreateCommand();
        insertData.Transaction = sqliteTransaction;
            insertData.CommandText = "INSERT INTO spatial_perf_data(id, x, y) VALUES ($id, $x, $y);";
            var idParameter = insertData.CreateParameter();
            idParameter.ParameterName = "$id";
            insertData.Parameters.Add(idParameter);
            var xParameter = insertData.CreateParameter();
            xParameter.ParameterName = "$x";
            insertData.Parameters.Add(xParameter);
            var yParameter = insertData.CreateParameter();
            yParameter.ParameterName = "$y";
            insertData.Parameters.Add(yParameter);

        await using var insertRtree = connection.CreateCommand();
        insertRtree.Transaction = sqliteTransaction;
            insertRtree.CommandText = "INSERT INTO spatial_perf_points(id, minX, maxX, minY, maxY) VALUES ($id, $x, $x, $y, $y);";
            var rtId = insertRtree.CreateParameter();
            rtId.ParameterName = "$id";
            insertRtree.Parameters.Add(rtId);
            var rtX = insertRtree.CreateParameter();
            rtX.ParameterName = "$x";
            insertRtree.Parameters.Add(rtX);
            var rtY = insertRtree.CreateParameter();
            rtY.ParameterName = "$y";
            insertRtree.Parameters.Add(rtY);

        for (var i = 0; i < fixture.Coordinates.Length; i++)
        {
            var coordinate = fixture.Coordinates[i];
            var x = coordinate[0];
            var y = coordinate[1];

            idParameter.Value = i + 1;
            xParameter.Value = x;
            yParameter.Value = y;
            await insertData.ExecuteNonQueryAsync().ConfigureAwait(false);

            rtId.Value = i + 1;
            rtX.Value = x;
            rtY.Value = y;
            await insertRtree.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await sqliteTransaction.CommitAsync().ConfigureAwait(false);

        var centerX = 0.5;
        var centerY = 0.5;
        var radius = 0.125;
        var minX = centerX - radius;
        var maxX = centerX + radius;
        var minY = centerY - radius;
        var maxY = centerY + radius;
        var radiusSquared = radius * radius;

        await using var candidateCommand = connection.CreateCommand();
        candidateCommand.CommandText = @"SELECT COUNT(*) FROM spatial_perf_points WHERE maxX >= $minX AND minX <= $maxX AND maxY >= $minY AND minY <= $maxY;";
        candidateCommand.Parameters.AddWithValue("$minX", minX);
        candidateCommand.Parameters.AddWithValue("$maxX", maxX);
        candidateCommand.Parameters.AddWithValue("$minY", minY);
        candidateCommand.Parameters.AddWithValue("$maxY", maxY);
        var candidateCount = (long)(await candidateCommand.ExecuteScalarAsync().ConfigureAwait(false));

        await using var queryCommand = connection.CreateCommand();
        queryCommand.CommandText = @"SELECT COUNT(*) FROM spatial_perf_data d JOIN spatial_perf_points r ON d.id = r.id WHERE r.maxX >= $minX AND r.minX <= $maxX AND r.maxY >= $minY AND r.minY <= $maxY AND ((d.x - $cx) * (d.x - $cx) + (d.y - $cy) * (d.y - $cy)) <= $radiusSquared;";
        queryCommand.Parameters.AddWithValue("$minX", minX);
        queryCommand.Parameters.AddWithValue("$maxX", maxX);
        queryCommand.Parameters.AddWithValue("$minY", minY);
        queryCommand.Parameters.AddWithValue("$maxY", maxY);
        queryCommand.Parameters.AddWithValue("$cx", centerX);
        queryCommand.Parameters.AddWithValue("$cy", centerY);
        queryCommand.Parameters.AddWithValue("$radiusSquared", radiusSquared);

        var stopwatch = Stopwatch.StartNew();
        var resultCount = (long)(await queryCommand.ExecuteScalarAsync().ConfigureAwait(false));
        stopwatch.Stop();

        return new PerfResult("SQLite RTree", stopwatch.Elapsed, candidateCount, resultCount, $"radius={radius:F3}");
    }

    private static async Task<PerfResult> BenchmarkPostgresAsync(LocalityFixture fixture, string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, "CREATE EXTENSION IF NOT EXISTS postgis;").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "DROP TABLE IF EXISTS spatial_perf_points;").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "CREATE TABLE spatial_perf_points(id INTEGER PRIMARY KEY, geom geometry(Point, 4326));").ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "CREATE INDEX spatial_perf_points_idx ON spatial_perf_points USING GIST (geom);").ConfigureAwait(false);

        await using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
        {
            for (var i = 0; i < fixture.Coordinates.Length; i++)
            {
                var coordinate = fixture.Coordinates[i];
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO spatial_perf_points(id, geom) VALUES (@id, ST_SetSRID(ST_MakePoint(@x, @y), 4326));";
                insert.Parameters.AddWithValue("@id", i + 1);
                insert.Parameters.AddWithValue("@x", coordinate[0]);
                insert.Parameters.AddWithValue("@y", coordinate[1]);
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }

        var centerX = 0.5;
        var centerY = 0.5;
        var radius = 0.125;

        await using var candidate = connection.CreateCommand();
        candidate.CommandText = "SELECT COUNT(*) FROM spatial_perf_points WHERE geom && ST_MakeEnvelope(@minX, @minY, @maxX, @maxY, 4326);";
        candidate.Parameters.AddWithValue("@minX", centerX - radius);
        candidate.Parameters.AddWithValue("@maxX", centerX + radius);
        candidate.Parameters.AddWithValue("@minY", centerY - radius);
        candidate.Parameters.AddWithValue("@maxY", centerY + radius);
        var candidateCount = (long)(await candidate.ExecuteScalarAsync().ConfigureAwait(false));

        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM spatial_perf_points WHERE ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@cx, @cy), 4326), @radius, false);";
        query.Parameters.AddWithValue("@cx", centerX);
        query.Parameters.AddWithValue("@cy", centerY);
        query.Parameters.AddWithValue("@radius", radius);

        var stopwatch = Stopwatch.StartNew();
        var resultCount = (long)(await query.ExecuteScalarAsync().ConfigureAwait(false));
        stopwatch.Stop();

        return new PerfResult("PostGIS", stopwatch.Elapsed, candidateCount, resultCount, $"radius={radius:F3}");
    }

    private static int CountCandidates(IReadOnlyList<ulong> codes, IReadOnlyList<SpatialIndexRange> ranges)
    {
        var total = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            if (ranges.Any(range => code >= range.Start && code <= range.End))
            {
                total++;
            }
        }

        return total;
    }

    private static int CountMatches(LocalityFixture fixture, Cartesian2DEngine engine, GeoPoint center, double radius)
    {
        var tolerance = engine.Options.DistanceTolerance;
        var matches = 0;
        foreach (var coordinate in fixture.Coordinates)
        {
            var point = new GeoPoint(coordinate[0], coordinate[1]);
            var distance = engine.Distance.Distance(point, center);
            if (distance <= radius + tolerance)
            {
                matches++;
            }
        }

        return matches;
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static bool TryGetEnv(string name, out string value)
    {
        value = Environment.GetEnvironmentVariable(name) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ResolveOutputPath()
    {
        var env = Environment.GetEnvironmentVariable(OutputEnv);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var relative = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "benchmarks.md");
        return Path.GetFullPath(relative);
    }

    private sealed class PerfSummary
    {
        private readonly List<PerfResult> _results = new();
        private readonly LocalityFixture _fixture;
        private readonly LocalityMetricsResult _metrics;

        public PerfSummary(LocalityFixture fixture, LocalityMetricsResult metrics)
        {
            _fixture = fixture;
            _metrics = metrics;
        }

        public void AddResult(PerfResult result)
        {
            _results.Add(result);
        }

        public void AddSkipped(string engine, string reason)
        {
            _results.Add(PerfResult.CreateSkipped(engine, reason));
        }

        public string ToMarkdown()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Spatial Benchmark Summary");
            builder.AppendLine();
            builder.AppendLine($"Fixture: `{_fixture.Id}` ({_fixture.Dimensions}D, precision {_fixture.PrecisionBits} bits, topK {_metrics.Overlap.EffectiveTopK})");
            builder.AppendLine($"Average Morton overlap: {_metrics.AverageOverlap.ToString("P2", CultureInfo.InvariantCulture)} (minimum {_metrics.MinimumOverlap.ToString("P2", CultureInfo.InvariantCulture)})");
            builder.AppendLine();
            builder.AppendLine("| Engine | Duration (ms) | Candidates | Results | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var result in _results)
            {
                if (result.Skipped)
                {
                    builder.AppendLine($"| {result.Engine} | skipped | - | - | {Escape(result.Notes)} |");
                }
                else
                {
                    builder.AppendLine($"| {result.Engine} | {result.Duration.TotalMilliseconds:F2} | {result.CandidateCount} | {result.ResultCount} | {Escape(result.Notes)} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine($"_Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC_");
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            return value.Replace("|", "\\|");
        }
    }

    private sealed record PerfResult(string Engine, TimeSpan Duration, long CandidateCount, long ResultCount, string Notes, bool Skipped = false)
    {
        public static PerfResult CreateSkipped(string engine, string reason) => new(engine, TimeSpan.Zero, 0, 0, reason, true);
    }
}
