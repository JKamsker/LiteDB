extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Perf;

public sealed class SpatialPerformanceHarness
{
    [Fact]
    public void CaptureLiteDbBenchmarkSnapshots()
    {
        if (!TryGetConfiguration(out var configuration))
        {
            return;
        }

        var fixtures = TestFixtureLoader.LoadLocalityFixtures();
        var snapshots = new List<PerfSnapshot>();

        foreach (var fixture in fixtures)
        {
            snapshots.Add(RunLiteDbScenario(fixture));
        }

        if (!string.IsNullOrWhiteSpace(configuration.ExternalMetricsPath) && File.Exists(configuration.ExternalMetricsPath))
        {
            var external = LoadExternalSnapshots(configuration.ExternalMetricsPath);
            snapshots.AddRange(external);
        }

        var markdown = PerfSummaryFormatter.BuildMarkdown(snapshots);

        if (configuration.WriteOutput)
        {
            var directory = Path.GetDirectoryName(configuration.OutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configuration.OutputPath, markdown);
        }

        // Ensure we produced LiteDB snapshots even when not writing to disk.
        snapshots.Should().Contain(s => s.Engine == "LiteDB");
    }

    private static PerfSnapshot RunLiteDbScenario(LocalityFixture fixture)
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());

        if (fixture.Dimensions == 2)
        {
            var collection = database.GetCollection<GridPoint2D>("points2d");
            var options = new SpatialIndexOptions(fixture.PrecisionBits, maxCoveringCells: 64);
            var domain = BoundingBox.From2D(0, 0, 1, 1);
            var descriptor = Spatial.UseCartesian2D(collection, point => point.Location, domain, options);
            Spatial.EnsurePointIndex(collection);

            var grid = LocalityTestHelpers.GenerateGrid(2, fixture.GridSize);
            var entities = grid.Select(coords => new GridPoint2D(new GeoPoint(coords[0], coords[1]))).ToList();
            collection.Insert(entities);
            descriptor = Spatial.EnsurePointIndex(collection);

            var resolver = new SpatialResolver();
            var runtimeDescriptor = descriptor.WithEngine(new Cartesian2DEngine(descriptor.GeometryFieldName, domain, options));
            resolver.RegisterDescriptor(runtimeDescriptor, nameof(GridPoint2D.Location));

            var center = new GeoPoint(0.5, 0.5);
            var radius = 0.3;
            var plan = PlanNear(resolver, center, radius);
            var explain = SpatialDiagnostics.Explain(plan, runtimeDescriptor);
            ExplainPlanAssertions.AssertIndexedPlan(explain);

            var mapper = runtimeDescriptor.Engine!.Mapper;
            var candidateCount = CountCandidates(entities.Select(point => mapper.Encode(point.Location)), plan.IndexRanges);
            var totalPoints = entities.Count;

            var stopwatch = Stopwatch.StartNew();
            var results = Spatial.Near(collection, p => p.Location, center, radius);
            stopwatch.Stop();

            return new PerfSnapshot(
                Scenario: $"Locality-{fixture.Dimensions}D",
                Engine: "LiteDB",
                CandidateCount: candidateCount,
                ResultCount: results.Count,
                TotalCount: totalPoints,
                Duration: stopwatch.Elapsed,
                Notes: "Cartesian2D Near");
        }

        if (fixture.Dimensions == 3)
        {
            var collection = database.GetCollection<GridPoint3D>("points3d");
            var options = new SpatialIndexOptions(fixture.PrecisionBits, maxCoveringCells: 128);
            var domain = BoundingBox.From3D(0, 0, 0, 1, 1, 1);
            var descriptor = Spatial.UseCartesian3D(collection, point => point.Location, domain, options);
            Spatial.EnsurePointIndex(collection);

            var grid = LocalityTestHelpers.GenerateGrid(3, fixture.GridSize);
            var entities = grid.Select(coords => new GridPoint3D(new GeoPoint3D(coords[0], coords[1], coords[2]))).ToList();
            collection.Insert(entities);
            descriptor = Spatial.EnsurePointIndex(collection);

            var resolver = new SpatialResolver();
            var runtimeDescriptor = descriptor.WithEngine(new Cartesian3DEngine(descriptor.GeometryFieldName, domain, options));
            resolver.RegisterDescriptor(runtimeDescriptor, nameof(GridPoint3D.Location));

            var center = new GeoPoint3D(0.5, 0.5, 0.5);
            var radius = 0.35;
            var plan = PlanNear(resolver, center, radius);
            var explain = SpatialDiagnostics.Explain(plan, runtimeDescriptor);
            ExplainPlanAssertions.AssertIndexedPlan(explain);

            var mapper = runtimeDescriptor.Engine!.Mapper;
            var candidateCount = CountCandidates(entities.Select(point => mapper.Encode(point.Location)), plan.IndexRanges);
            var totalPoints = entities.Count;

            var stopwatch = Stopwatch.StartNew();
            var results = Spatial.Near(collection, p => p.Location, center, radius);
            stopwatch.Stop();

            return new PerfSnapshot(
                Scenario: $"Locality-{fixture.Dimensions}D",
                Engine: "LiteDB",
                CandidateCount: candidateCount,
                ResultCount: results.Count,
                TotalCount: totalPoints,
                Duration: stopwatch.Elapsed,
                Notes: "Cartesian3D Near");
        }

        throw new InvalidOperationException($"Unsupported fixture dimensionality: {fixture.Dimensions}");
    }

    private static ISpatialQueryPlan PlanNear(SpatialResolver resolver, GeoPoint center, double radius)
    {
        Expression<Func<GridPoint2D, bool>> predicate = point => SpatialExpressions.Near(point.Location, center, radius);
        var methodCall = (MethodCallExpression)predicate.Body;
        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();
        return plan!;
    }

    private static ISpatialQueryPlan PlanNear(SpatialResolver resolver, GeoPoint3D center, double radius)
    {
        Expression<Func<GridPoint3D, bool>> predicate = point => SpatialExpressions.Near(point.Location, center, radius);
        var methodCall = (MethodCallExpression)predicate.Body;
        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();
        return plan!;
    }

    private static long CountCandidates(IEnumerable<ulong> codes, IReadOnlyList<SpatialIndexRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return 0;
        }

        long total = 0;
        foreach (var code in codes)
        {
            if (ranges.Any(range => code >= range.Start && code <= range.End))
            {
                total++;
            }
        }

        return total;
    }

    private static IReadOnlyList<PerfSnapshot> LoadExternalSnapshots(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ExternalPerfDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return document?.Scenarios ?? Array.Empty<PerfSnapshot>();
    }

    private static bool TryGetConfiguration(out PerfConfiguration configuration)
    {
        var enabled = Environment.GetEnvironmentVariable("LITEDB_SPATIAL_PERF");
        if (!bool.TryParse(enabled, out var run) || !run)
        {
            configuration = default!;
            return false;
        }

        var write = Environment.GetEnvironmentVariable("LITEDB_SPATIAL_PERF_WRITE");
        var output = Environment.GetEnvironmentVariable("LITEDB_SPATIAL_PERF_OUTPUT");
        var external = Environment.GetEnvironmentVariable("LITEDB_SPATIAL_PERF_EXTERNAL");

        if (string.IsNullOrWhiteSpace(output))
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            output = Path.Combine(root, "docs", "benchmarks.md");
        }

        configuration = new PerfConfiguration(output, external, bool.TryParse(write, out var writeFlag) && writeFlag);
        return true;
    }

    private sealed record GridPoint2D(GeoPoint Location);

    private sealed record GridPoint3D(GeoPoint3D Location);

    internal sealed record PerfSnapshot(string Scenario, string Engine, long CandidateCount, int ResultCount, int TotalCount, TimeSpan Duration, string Notes)
    {
        public double CandidateRatio => TotalCount == 0 ? 0 : CandidateCount / (double)TotalCount;
    }

    private sealed record PerfConfiguration(string OutputPath, string? ExternalMetricsPath, bool WriteOutput)
    {
        public string OutputPath { get; } = OutputPath;
        public string? ExternalMetricsPath { get; } = ExternalMetricsPath;
        public bool WriteOutput { get; } = WriteOutput;
    }

    private sealed record ExternalPerfDocument(IReadOnlyList<PerfSnapshot> Scenarios);
}

file static class PerfSummaryFormatter
{
    public static string BuildMarkdown(IReadOnlyList<SpatialPerformanceHarness.PerfSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return "# Spatial benchmark snapshots\n\nNo perf results captured.";
        }

        var grouped = snapshots
            .GroupBy(s => s.Scenario)
            .OrderBy(g => g.Key)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# Spatial benchmark snapshots");
        builder.AppendLine();
        builder.AppendLine($"_Last updated: {DateTime.UtcNow:yyyy-MM-dd}_");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Engine | Candidates | Reduction | Duration (ms) | Results | Notes |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | --- |");

        foreach (var group in grouped)
        {
            foreach (var snapshot in group.OrderBy(s => s.Engine))
            {
                builder.Append('|')
                    .Append(' ')
                    .Append(group.Key)
                    .Append(" | ")
                    .Append(snapshot.Engine)
                    .Append(" | ")
                    .Append(snapshot.CandidateCount)
                    .Append(" | ")
                    .Append(snapshot.CandidateRatio.ToString("0.###"))
                    .Append(" | ")
                    .Append(snapshot.Duration.TotalMilliseconds.ToString("0.##"))
                    .Append(" | ")
                    .Append(snapshot.ResultCount)
                    .Append(" | ")
                    .Append(snapshot.Notes)
                    .AppendLine(" |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("> Benchmarks are best-effort and should be re-run on the target hardware before publishing numbers externally.");
        return builder.ToString();
    }
}
