#nullable enable

extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Npgsql;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Perf;

public sealed class SpatialPerfHarness
{
    [Fact]
    public void RunSpatialBenchmarks()
    {
        if (!PerfTestEnvironment.IsEnabled)
        {
            return;
        }

        var fixtures = MortonLocalityFixtures.Load();
        var results = new List<BenchmarkResult>();

        foreach (var @case in fixtures.Cases)
        {
            results.Add(RunLiteDbBenchmark(@case));

            if (PerfTestEnvironment.TryGetSqliteConnection(out var sqliteConnection))
            {
                results.Add(RunSqliteBenchmark(@case, sqliteConnection));
            }

            if (PerfTestEnvironment.TryGetPostgresConnection(out var postgresConnection))
            {
                results.Add(RunPostgresBenchmark(@case, postgresConnection));
            }
        }

        BenchmarkDocumenter.PersistResults(results);
    }

    private static BenchmarkResult RunLiteDbBenchmark(MortonLocalityCase @case)
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var codes = @case.GetExpectedCodes();
        var points = @case.GeneratePoints();

        if (@case.Dimensions == 2)
        {
            var domain = BoundingBox.From2D(0, 0, 1, 1);
            var options = new SpatialIndexOptions(precisionBits: @case.PrecisionBits, maxCoveringCells: 256);
            var collection = database.GetCollection<CartesianPoint2D>("perf_points2d");
            var descriptor = Spatial.UseCartesian2D(collection, x => x.Position, domain, options);
            collection.Insert(points.Select(p => new CartesianPoint2D(new GeoPoint(p[0], p[1]))));
            Spatial.EnsurePointIndex(collection);

            var center = new GeoPoint(@case.QueryCenter[0], @case.QueryCenter[1]);
            var radius = @case.QueryRadius;

            var stopwatch = Stopwatch.StartNew();
            var rows = Spatial.Near(collection, x => x.Position, center, radius);
            stopwatch.Stop();

            var plan = SpatialCartesian2D.Near(descriptor, center, radius);
            var reduction = LocalityMetrics.MeasureCandidateReduction(plan.IndexRanges, codes);

            return new BenchmarkResult(
                "LiteDB",
                @case.Name,
                "Near",
                reduction.TotalCandidates,
                rows.Count,
                stopwatch.Elapsed,
                reduction.IndexCandidates,
                reduction.ReductionRatio,
                Notes: $"Plan ranges: {plan.IndexRanges.Count}");
        }

        if (@case.Dimensions == 3)
        {
            var domain = BoundingBox.From3D(0, 0, 0, 1, 1, 1);
            var options = new SpatialIndexOptions(precisionBits: @case.PrecisionBits, maxCoveringCells: 256);
            var collection = database.GetCollection<CartesianPoint3D>("perf_points3d");
            var descriptor = Spatial.UseCartesian3D(collection, x => x.Position, domain, options);
            collection.Insert(points.Select(p => new CartesianPoint3D(new GeoPoint3D(p[0], p[1], p[2]))));
            Spatial.EnsurePointIndex(collection);

            var center = new GeoPoint3D(@case.QueryCenter[0], @case.QueryCenter[1], @case.QueryCenter[2]);
            var radius = @case.QueryRadius;

            var stopwatch = Stopwatch.StartNew();
            var rows = Spatial.Near(collection, x => x.Position, center, radius);
            stopwatch.Stop();

            var plan = SpatialCartesian3D.Near(descriptor, center, radius);
            var reduction = LocalityMetrics.MeasureCandidateReduction(plan.IndexRanges, codes);

            return new BenchmarkResult(
                "LiteDB",
                @case.Name,
                "Near",
                reduction.TotalCandidates,
                rows.Count,
                stopwatch.Elapsed,
                reduction.IndexCandidates,
                reduction.ReductionRatio,
                Notes: $"Plan ranges: {plan.IndexRanges.Count}");
        }

        throw new NotSupportedException($"Unsupported dimensionality '{@case.Dimensions}' in fixture '{@case.Name}'.");
    }

    private static BenchmarkResult RunSqliteBenchmark(MortonLocalityCase @case, string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var tableName = $"spatial_perf_{@case.Dimensions}d";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"DROP TABLE IF EXISTS {tableName};";
            command.ExecuteNonQuery();
            command.CommandText = @case.Dimensions == 2
                ? $"CREATE VIRTUAL TABLE {tableName} USING rtree(id, minX, maxX, minY, maxY);"
                : $"CREATE VIRTUAL TABLE {tableName} USING rtree(id, minX, maxX, minY, maxY, minZ, maxZ);";
            command.ExecuteNonQuery();
        }

        var points = @case.GeneratePoints();
        using (var transaction = connection.BeginTransaction())
        {
            for (var index = 0; index < points.Count; index++)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;

                if (@case.Dimensions == 2)
                {
                    insert.CommandText = $"INSERT INTO {tableName} VALUES ($id, $minX, $maxX, $minY, $maxY);";
                    insert.Parameters.AddWithValue("$id", index);
                    insert.Parameters.AddWithValue("$minX", points[index][0]);
                    insert.Parameters.AddWithValue("$maxX", points[index][0]);
                    insert.Parameters.AddWithValue("$minY", points[index][1]);
                    insert.Parameters.AddWithValue("$maxY", points[index][1]);
                }
                else
                {
                    insert.CommandText = $"INSERT INTO {tableName} VALUES ($id, $minX, $maxX, $minY, $maxY, $minZ, $maxZ);";
                    insert.Parameters.AddWithValue("$id", index);
                    insert.Parameters.AddWithValue("$minX", points[index][0]);
                    insert.Parameters.AddWithValue("$maxX", points[index][0]);
                    insert.Parameters.AddWithValue("$minY", points[index][1]);
                    insert.Parameters.AddWithValue("$maxY", points[index][1]);
                    insert.Parameters.AddWithValue("$minZ", points[index][2]);
                    insert.Parameters.AddWithValue("$maxZ", points[index][2]);
                }

                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        var (minBounds, maxBounds) = BuildBounds(@case);

        using var query = connection.CreateCommand();
        if (@case.Dimensions == 2)
        {
            query.CommandText = $"SELECT id FROM {tableName} WHERE maxX >= $minX AND minX <= $maxX AND maxY >= $minY AND minY <= $maxY;";
        }
        else
        {
            query.CommandText = $"SELECT id FROM {tableName} WHERE maxX >= $minX AND minX <= $maxX AND maxY >= $minY AND minY <= $maxY AND maxZ >= $minZ AND minZ <= $maxZ;";
            query.Parameters.AddWithValue("$minZ", minBounds[2]);
            query.Parameters.AddWithValue("$maxZ", maxBounds[2]);
        }

        query.Parameters.AddWithValue("$minX", minBounds[0]);
        query.Parameters.AddWithValue("$maxX", maxBounds[0]);
        query.Parameters.AddWithValue("$minY", minBounds[1]);
        query.Parameters.AddWithValue("$maxY", maxBounds[1]);

        var stopwatch = Stopwatch.StartNew();
        var ids = new List<int>();
        using (var reader = query.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(reader.GetInt32(0));
            }
        }

        stopwatch.Stop();

        var total = points.Count;
        var candidateCount = ids.Count;
        var reduction = total == 0 ? (double?)null : 1d - candidateCount / (double)total;

        return new BenchmarkResult(
            "SQLite RTree",
            @case.Name,
            "BoundingBox",
            total,
            candidateCount,
            stopwatch.Elapsed,
            candidateCount,
            reduction,
            Notes: "RTree bounding-box prefilter");
    }

    private static BenchmarkResult RunPostgresBenchmark(MortonLocalityCase @case, string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis;";
            command.ExecuteNonQuery();
        }

        var tableName = $"spatial_perf_{@case.Dimensions}d_{Guid.NewGuid():N}";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"DROP TABLE IF EXISTS {tableName};";
            command.ExecuteNonQuery();
            command.CommandText = @case.Dimensions == 2
                ? $"CREATE TABLE {tableName} (id SERIAL PRIMARY KEY, geom geometry(Point, 0));"
                : $"CREATE TABLE {tableName} (id SERIAL PRIMARY KEY, geom geometry(PointZ, 0));";
            command.ExecuteNonQuery();
        }

        var points = @case.GeneratePoints();
        using (var transaction = connection.BeginTransaction())
        {
            for (var index = 0; index < points.Count; index++)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                if (@case.Dimensions == 2)
                {
                    insert.CommandText = $"INSERT INTO {tableName} (geom) VALUES (ST_MakePoint($x, $y));";
                    insert.Parameters.AddWithValue("$x", points[index][0]);
                    insert.Parameters.AddWithValue("$y", points[index][1]);
                }
                else
                {
                    insert.CommandText = $"INSERT INTO {tableName} (geom) VALUES (ST_MakePoint($x, $y, $z));";
                    insert.Parameters.AddWithValue("$x", points[index][0]);
                    insert.Parameters.AddWithValue("$y", points[index][1]);
                    insert.Parameters.AddWithValue("$z", points[index][2]);
                }

                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        var stopwatch = Stopwatch.StartNew();
        using var query = connection.CreateCommand();
        if (@case.Dimensions == 2)
        {
            query.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE ST_DWithin(geom, ST_MakePoint($x, $y), $radius);";
            query.Parameters.AddWithValue("$x", @case.QueryCenter[0]);
            query.Parameters.AddWithValue("$y", @case.QueryCenter[1]);
        }
        else
        {
            query.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE ST_3DDWithin(geom, ST_MakePoint($x, $y, $z), $radius);";
            query.Parameters.AddWithValue("$x", @case.QueryCenter[0]);
            query.Parameters.AddWithValue("$y", @case.QueryCenter[1]);
            query.Parameters.AddWithValue("$z", @case.QueryCenter[2]);
        }

        query.Parameters.AddWithValue("$radius", @case.QueryRadius);
        var count = Convert.ToInt32(query.ExecuteScalar());
        stopwatch.Stop();

        using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName};";
            cleanup.ExecuteNonQuery();
        }

        var total = points.Count;
        var reduction = total == 0 ? (double?)null : 1d - count / (double)total;

        return new BenchmarkResult(
            "PostGIS",
            @case.Name,
            "ST_DWithin",
            total,
            count,
            stopwatch.Elapsed,
            count,
            reduction,
            Notes: "Distance evaluated via PostGIS");
    }

    private static (double[] Min, double[] Max) BuildBounds(MortonLocalityCase @case)
    {
        var center = @case.QueryCenter;
        var radius = @case.QueryRadius;
        var min = new double[@case.Dimensions];
        var max = new double[@case.Dimensions];

        for (var axis = 0; axis < @case.Dimensions; axis++)
        {
            min[axis] = Math.Max(0d, center[axis] - radius);
            max[axis] = Math.Min(1d, center[axis] + radius);
        }

        return (min, max);
    }

    private sealed record CartesianPoint2D(GeoPoint Position);

    private sealed record CartesianPoint3D(GeoPoint3D Position);
}
