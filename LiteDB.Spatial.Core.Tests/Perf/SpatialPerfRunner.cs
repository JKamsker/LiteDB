#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests;
using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;

namespace LiteDB.Spatial.Core.Tests.Perf;

internal sealed class SpatialPerfRunner
{
    private readonly IReadOnlyList<LocalityFixtureCase> _cases;
    private readonly string? _sqliteConnectionString;
    private readonly string? _postgisConnectionString;
    private readonly bool _writeSnapshots;

    public SpatialPerfRunner(IReadOnlyList<LocalityFixtureCase> cases)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _sqliteConnectionString = Environment.GetEnvironmentVariable("LITEDB_SPATIAL_SQLITE");
        _postgisConnectionString = Environment.GetEnvironmentVariable("LITEDB_SPATIAL_POSTGIS");
        _writeSnapshots = string.Equals(Environment.GetEnvironmentVariable("LITEDB_SPATIAL_PERF_WRITE"), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("LITEDB_SPATIAL_PERF_WRITE"), "true", StringComparison.OrdinalIgnoreCase);
    }

    public SpatialPerfSummary Execute()
    {
        var results = new List<SpatialPerfEngineResult>
        {
            RunLiteDb(),
            RunSqlite(),
            RunPostgis()
        };

        var summary = new SpatialPerfSummary(DateTimeOffset.UtcNow, results);

        if (_writeSnapshots)
        {
            var path = ResolveRepositoryPath("docs", "benchmarks.md");
            BenchmarkSnapshotWriter.Update(summary, path);
        }

        return summary;
    }

    private SpatialPerfEngineResult RunLiteDb()
    {
        var observations = new List<SpatialPerfObservation>();
        var stopwatch = Stopwatch.StartNew();

        foreach (var fixture in _cases)
        {
            var points = LocalityTestSupport.GenerateGrid(fixture.Shape);
            var sorted = BuildSortedCodes(fixture.Codes);
            var queries = BuildQueries(fixture);
            var options = new SpatialIndexOptions(fixture.PrecisionBits);
            SpatialCollectionDescriptor descriptor;
            ISpatialEngine engine;

            if (fixture.Dimensions == 2)
            {
                engine = new Cartesian2DEngine("geometry", BoundingBox.From2D(0, 0, 1, 1), options);
                descriptor = new SpatialCollectionDescriptor(fixture.Name, engine.Name, 2, "geometry", options).WithEngine(engine);
            }
            else
            {
                engine = new Cartesian3DEngine("geometry", BoundingBox.From3D(0, 0, 0, 1, 1, 1), options);
                descriptor = new SpatialCollectionDescriptor(fixture.Name, engine.Name, 3, "geometry", options).WithEngine(engine);
            }

            foreach (var query in queries)
            {
                observations.Add(RunLiteDbQuery(fixture, engine, descriptor, points, sorted, query));
            }
        }

        stopwatch.Stop();
        return new SpatialPerfEngineResult("LiteDB", PerfEngineStatus.Completed, observations, stopwatch.Elapsed, "In-process Cartesian engines");
    }

    private SpatialPerfObservation RunLiteDbQuery(
        LocalityFixtureCase fixture,
        ISpatialEngine engine,
        SpatialCollectionDescriptor descriptor,
        IReadOnlyList<double[]> points,
        List<(ulong Code, int Index)> sorted,
        SpatialQueryDefinition query)
    {
        var totalPoints = points.Count;
        var queryTimer = Stopwatch.StartNew();

        ISpatialQueryPlan plan;
        if (fixture.Dimensions == 2)
        {
            var center = new GeoPoint(query.Center[1], query.Center[0]);
            plan = engine.PlanNear(center, query.Radius);
        }
        else
        {
            var center = new GeoPoint3D(query.Center[0], query.Center[1], query.Center[2]);
            plan = engine.PlanNear(center, query.Radius);
        }

        queryTimer.Stop();

        var explain = SpatialDiagnostics.Explain(plan, descriptor);
        explain.ShouldHighlightIndexPrefilters();

        var candidateIndices = CollectCandidates(sorted, plan.IndexRanges);
        var exactMatches = EvaluateExactMatches(points, query.Center, query.Radius);

        return SpatialPerfObservation.Create(
            fixture.Name,
            query.Label,
            totalPoints,
            candidateIndices.Count,
            exactMatches.Count,
            queryTimer.Elapsed);
    }

    private SpatialPerfEngineResult RunSqlite()
    {
        var observations = new List<SpatialPerfObservation>();
        var stopwatch = Stopwatch.StartNew();

        foreach (var fixture in _cases.Where(c => c.Dimensions == 2))
        {
            var connectionString = string.IsNullOrWhiteSpace(_sqliteConnectionString) ? "Data Source=:memory:" : _sqliteConnectionString!;
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE VIRTUAL TABLE spatial_points USING rtree(id, minX, maxX, minY, maxY);";
                command.ExecuteNonQuery();
            }

            var points = LocalityTestSupport.GenerateGrid(fixture.Shape);
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO spatial_points VALUES ($id, $minX, $maxX, $minY, $maxY);";
                var id = insert.CreateParameter();
                id.ParameterName = "$id";
                insert.Parameters.Add(id);
                var minX = insert.CreateParameter();
                minX.ParameterName = "$minX";
                insert.Parameters.Add(minX);
                var maxX = insert.CreateParameter();
                maxX.ParameterName = "$maxX";
                insert.Parameters.Add(maxX);
                var minY = insert.CreateParameter();
                minY.ParameterName = "$minY";
                insert.Parameters.Add(minY);
                var maxY = insert.CreateParameter();
                maxY.ParameterName = "$maxY";
                insert.Parameters.Add(maxY);

                for (var i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    id.Value = i;
                    minX.Value = point[0];
                    maxX.Value = point[0];
                    minY.Value = point[1];
                    maxY.Value = point[1];
                    insert.ExecuteNonQuery();
                }
            }

            var sorted = BuildSortedCodes(fixture.Codes);
            var queries = BuildQueries(fixture);

            foreach (var query in queries)
            {
                observations.Add(RunSqliteQuery(connection, fixture, points, sorted, query));
            }
        }

        stopwatch.Stop();

        var status = observations.Count > 0 ? PerfEngineStatus.Completed : PerfEngineStatus.Skipped;
        var details = string.IsNullOrWhiteSpace(_sqliteConnectionString) ? "In-memory SQLite" : _sqliteConnectionString!;
        return new SpatialPerfEngineResult("SQLite RTree", status, observations, stopwatch.Elapsed, details);
    }

    private SpatialPerfObservation RunSqliteQuery(
        SqliteConnection connection,
        LocalityFixtureCase fixture,
        IReadOnlyList<double[]> points,
        List<(ulong Code, int Index)> sorted,
        SpatialQueryDefinition query)
    {
        var totalPoints = points.Count;
        var timer = Stopwatch.StartNew();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM spatial_points WHERE minX <= $maxX AND maxX >= $minX AND minY <= $maxY AND maxY >= $minY;";
        command.Parameters.AddWithValue("$minX", query.Center[0] - query.Radius);
        command.Parameters.AddWithValue("$maxX", query.Center[0] + query.Radius);
        command.Parameters.AddWithValue("$minY", query.Center[1] - query.Radius);
        command.Parameters.AddWithValue("$maxY", query.Center[1] + query.Radius);

        var candidateIndices = new HashSet<int>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                candidateIndices.Add(reader.GetInt32(0));
            }
        }

        timer.Stop();

        var exactMatches = EvaluateExactMatches(points, query.Center, query.Radius);
        return SpatialPerfObservation.Create(
            fixture.Name,
            query.Label,
            totalPoints,
            candidateIndices.Count,
            exactMatches.Count,
            timer.Elapsed);
    }

    private SpatialPerfEngineResult RunPostgis()
    {
        if (string.IsNullOrWhiteSpace(_postgisConnectionString))
        {
            return SpatialPerfEngineResult.Skipped("PostGIS", "Set LITEDB_SPATIAL_POSTGIS to compare against PostgreSQL");
        }

        var observations = new List<SpatialPerfObservation>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new NpgsqlConnection(_postgisConnectionString);
            connection.Open();

            using (var ensure = connection.CreateCommand())
            {
                ensure.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis;";
                ensure.ExecuteNonQuery();
            }

            foreach (var fixture in _cases.Where(c => c.Dimensions == 2))
            {
                using (var drop = connection.CreateCommand())
                {
                    drop.CommandText = "DROP TABLE IF EXISTS spatial_points;";
                    drop.ExecuteNonQuery();
                }

                using (var create = connection.CreateCommand())
                {
                    create.CommandText = "CREATE TEMP TABLE spatial_points(id integer PRIMARY KEY, geom geometry(Point, 4326));";
                    create.ExecuteNonQuery();
                }

                var points = LocalityTestSupport.GenerateGrid(fixture.Shape);
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = "INSERT INTO spatial_points(id, geom) VALUES ($id, ST_SetSRID(ST_MakePoint($lon, $lat), 4326));";
                    var id = insert.Parameters.Add("$id", NpgsqlDbType.Integer);
                    var lon = insert.Parameters.Add("$lon", NpgsqlDbType.Double);
                    var lat = insert.Parameters.Add("$lat", NpgsqlDbType.Double);

                    for (var i = 0; i < points.Count; i++)
                    {
                        var point = points[i];
                        id.Value = i;
                        lon.Value = point[0];
                        lat.Value = point[1];
                        insert.ExecuteNonQuery();
                    }
                }

                var queries = BuildQueries(fixture);

                foreach (var query in queries)
                {
                    observations.Add(RunPostgisQuery(connection, fixture, points, query));
                }
            }

            stopwatch.Stop();
            var status = observations.Count > 0 ? PerfEngineStatus.Completed : PerfEngineStatus.Skipped;
            return new SpatialPerfEngineResult("PostGIS", status, observations, stopwatch.Elapsed, _postgisConnectionString!);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new SpatialPerfEngineResult("PostGIS", PerfEngineStatus.Error, observations, stopwatch.Elapsed, ex.Message);
        }
    }

    private SpatialPerfObservation RunPostgisQuery(
        NpgsqlConnection connection,
        LocalityFixtureCase fixture,
        IReadOnlyList<double[]> points,
        SpatialQueryDefinition query)
    {
        var totalPoints = points.Count;
        var timer = Stopwatch.StartNew();

        using var candidate = connection.CreateCommand();
        candidate.CommandText = @"SELECT id FROM spatial_points WHERE geom && ST_MakeEnvelope($minLon, $minLat, $maxLon, $maxLat, 4326);";
        candidate.Parameters.AddWithValue("$minLon", query.Center[0] - query.Radius);
        candidate.Parameters.AddWithValue("$minLat", query.Center[1] - query.Radius);
        candidate.Parameters.AddWithValue("$maxLon", query.Center[0] + query.Radius);
        candidate.Parameters.AddWithValue("$maxLat", query.Center[1] + query.Radius);

        var candidateIndices = new HashSet<int>();
        using (var reader = candidate.ExecuteReader())
        {
            while (reader.Read())
            {
                candidateIndices.Add(reader.GetInt32(0));
            }
        }

        using var exact = connection.CreateCommand();
        exact.CommandText = "SELECT id FROM spatial_points WHERE ST_DWithin(geom, ST_SetSRID(ST_MakePoint($lon, $lat), 4326), $radius);";
        exact.Parameters.AddWithValue("$lon", query.Center[0]);
        exact.Parameters.AddWithValue("$lat", query.Center[1]);
        exact.Parameters.AddWithValue("$radius", query.Radius);

        var exactMatches = new HashSet<int>();
        using (var reader = exact.ExecuteReader())
        {
            while (reader.Read())
            {
                exactMatches.Add(reader.GetInt32(0));
            }
        }

        timer.Stop();

        return SpatialPerfObservation.Create(
            fixture.Name,
            query.Label,
            totalPoints,
            candidateIndices.Count,
            exactMatches.Count,
            timer.Elapsed);
    }

    private static List<(ulong Code, int Index)> BuildSortedCodes(IReadOnlyList<ulong> codes)
    {
        return codes
            .Select((code, index) => (Code: code, Index: index))
            .OrderBy(tuple => tuple.Code)
            .ToList();
    }

    private static HashSet<int> CollectCandidates(List<(ulong Code, int Index)> sorted, IReadOnlyList<SpatialIndexRange> ranges)
    {
        var results = new HashSet<int>();
        foreach (var range in ranges)
        {
            var start = LowerBound(sorted, range.Start);
            var end = UpperBound(sorted, range.End);
            for (var i = start; i < end; i++)
            {
                results.Add(sorted[i].Index);
            }
        }

        return results;
    }

    private static HashSet<int> EvaluateExactMatches(IReadOnlyList<double[]> points, IReadOnlyList<double> center, double radius)
    {
        var matches = new HashSet<int>();
        var radiusSquared = radius * radius;

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var distanceSquared = 0d;
            for (var axis = 0; axis < center.Count; axis++)
            {
                var delta = point[axis] - center[axis];
                distanceSquared += delta * delta;
            }

            if (distanceSquared <= radiusSquared)
            {
                matches.Add(i);
            }
        }

        return matches;
    }

    private static int LowerBound(List<(ulong Code, int Index)> sorted, ulong value)
    {
        var lo = 0;
        var hi = sorted.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (sorted[mid].Code < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static int UpperBound(List<(ulong Code, int Index)> sorted, ulong value)
    {
        var lo = 0;
        var hi = sorted.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (sorted[mid].Code <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static IReadOnlyList<SpatialQueryDefinition> BuildQueries(LocalityFixtureCase fixture)
    {
        var mid = Enumerable.Repeat(0.5, fixture.Dimensions).ToArray();
        var inner = Enumerable.Repeat(0.25, fixture.Dimensions).ToArray();
        var outer = Enumerable.Repeat(0.75, fixture.Dimensions).ToArray();

        var radii = fixture.Dimensions == 2
            ? new[] { 0.08, 0.12, 0.18 }
            : new[] { 0.12, 0.18, 0.24 };

        return new[]
        {
            new SpatialQueryDefinition($"center_{radii[0]:0.00}", mid, radii[0]),
            new SpatialQueryDefinition($"inner_{radii[1]:0.00}", inner, radii[1]),
            new SpatialQueryDefinition($"outer_{radii[2]:0.00}", outer, radii[2])
        };
    }

    private static string ResolveRepositoryPath(params string[] segments)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        return Path.Combine(new[] { repoRoot }.Concat(segments).ToArray());
    }
}

internal sealed record SpatialQueryDefinition(string Label, IReadOnlyList<double> Center, double Radius);

internal sealed class SpatialPerfSummary
{
    public SpatialPerfSummary(DateTimeOffset timestamp, IReadOnlyList<SpatialPerfEngineResult> engines)
    {
        Timestamp = timestamp;
        Engines = engines;
    }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyList<SpatialPerfEngineResult> Engines { get; }
}

internal sealed class SpatialPerfEngineResult
{
    public SpatialPerfEngineResult(string engine, PerfEngineStatus status, IReadOnlyList<SpatialPerfObservation> observations, TimeSpan elapsed, string details)
    {
        Engine = engine;
        Status = status;
        Observations = observations;
        Elapsed = elapsed;
        Details = details;

        if (observations.Count > 0)
        {
            AverageCandidateCount = observations.Average(o => o.CandidateCount);
            AverageCandidateRatio = observations.Average(o => o.CandidateRatio);
            AverageReduction = observations.Average(o => o.Reduction);
            AveragePrecision = observations.Average(o => o.Precision);
            AverageDurationMs = observations.Average(o => o.Duration.TotalMilliseconds);
        }
    }

    public static SpatialPerfEngineResult Skipped(string engine, string details)
    {
        return new SpatialPerfEngineResult(engine, PerfEngineStatus.Skipped, Array.Empty<SpatialPerfObservation>(), TimeSpan.Zero, details);
    }

    public string Engine { get; }

    public PerfEngineStatus Status { get; }

    public IReadOnlyList<SpatialPerfObservation> Observations { get; }

    public TimeSpan Elapsed { get; }

    public string Details { get; }

    public double AverageCandidateCount { get; }

    public double AverageCandidateRatio { get; }

    public double AverageReduction { get; }

    public double AveragePrecision { get; }

    public double AverageDurationMs { get; }
}

internal enum PerfEngineStatus
{
    Completed,
    Skipped,
    Error
}

internal readonly struct SpatialPerfObservation
{
    private SpatialPerfObservation(string fixture, string query, int totalPoints, int candidateCount, int exactCount, double candidateRatio, double reduction, double precision, TimeSpan duration)
    {
        Fixture = fixture;
        Query = query;
        TotalPoints = totalPoints;
        CandidateCount = candidateCount;
        ExactCount = exactCount;
        CandidateRatio = candidateRatio;
        Reduction = reduction;
        Precision = precision;
        Duration = duration;
    }

    public string Fixture { get; }

    public string Query { get; }

    public int TotalPoints { get; }

    public int CandidateCount { get; }

    public int ExactCount { get; }

    public double CandidateRatio { get; }

    public double Reduction { get; }

    public double Precision { get; }

    public TimeSpan Duration { get; }

    public static SpatialPerfObservation Create(string fixture, string query, int totalPoints, int candidateCount, int exactCount, TimeSpan duration)
    {
        var ratio = totalPoints == 0 ? 0d : candidateCount / (double)totalPoints;
        var reduction = 1d - ratio;
        var precision = candidateCount == 0 ? 0d : Math.Min(1d, exactCount / (double)candidateCount);
        return new SpatialPerfObservation(fixture, query, totalPoints, candidateCount, exactCount, ratio, reduction, precision, duration);
    }
}

internal static class BenchmarkSnapshotWriter
{
    private const string StartMarker = "<!-- benchmarks:start -->";
    private const string EndMarker = "<!-- benchmarks:end -->";

    public static void Update(SpatialPerfSummary summary, string documentationPath)
    {
        if (summary == null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        var table = BuildTable(summary);
        Directory.CreateDirectory(Path.GetDirectoryName(documentationPath)!);

        string content;
        if (File.Exists(documentationPath))
        {
            content = File.ReadAllText(documentationPath);
            if (!content.Contains(StartMarker, StringComparison.Ordinal) || !content.Contains(EndMarker, StringComparison.Ordinal))
            {
                content += Environment.NewLine + StartMarker + Environment.NewLine + EndMarker + Environment.NewLine;
            }
        }
        else
        {
            content = "# Spatial Benchmark Snapshots" + Environment.NewLine + Environment.NewLine
                + "This file is generated by the spatial performance harness. Do not edit between the markers." + Environment.NewLine + Environment.NewLine
                + StartMarker + Environment.NewLine + EndMarker + Environment.NewLine;
        }

        var startIndex = content.IndexOf(StartMarker, StringComparison.Ordinal);
        var endIndex = content.IndexOf(EndMarker, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
        {
            throw new InvalidOperationException("Benchmark markers are missing or malformed in docs/benchmarks.md.");
        }

        var before = content[..(startIndex + StartMarker.Length)];
        var after = content[endIndex..];
        var generated = before
            + Environment.NewLine + Environment.NewLine
            + $"_Last generated: {summary.Timestamp:O}_" + Environment.NewLine + Environment.NewLine
            + table + Environment.NewLine + Environment.NewLine;

        File.WriteAllText(documentationPath, generated + after);
    }

    private static string BuildTable(SpatialPerfSummary summary)
    {
        var lines = new List<string>
        {
            "| Engine | Queries | Avg Candidates | Avg Reduction | Avg Precision | Avg ms | Status | Details |",
            "| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        };

        foreach (var result in summary.Engines)
        {
            var queries = result.Observations.Count;
            var avgCandidates = queries > 0 ? result.AverageCandidateCount.ToString("0.00") : "n/a";
            var avgReduction = queries > 0 ? result.AverageReduction.ToString("0.000") : "n/a";
            var avgPrecision = queries > 0 ? result.AveragePrecision.ToString("0.000") : "n/a";
            var avgMs = queries > 0 ? result.AverageDurationMs.ToString("0.00") : "n/a";
            var details = string.IsNullOrWhiteSpace(result.Details) ? "n/a" : result.Details.Replace("|", "\\|");

            lines.Add($"| {result.Engine} | {queries} | {avgCandidates} | {avgReduction} | {avgPrecision} | {avgMs} | {result.Status} | {details} |");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
