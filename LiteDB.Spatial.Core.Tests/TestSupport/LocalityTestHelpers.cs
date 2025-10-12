using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class LocalityTestHelpers
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<LocalityFixtureDefinition> LoadDefinitions()
    {
        var root = TestResourceLocator.RepositoryRoot;
        var path = Path.Combine(root, "LiteDB.Spatial.Core.Tests", "Indexing", "Fixtures", "locality-fixtures.json");
        var json = File.ReadAllText(path);
        var payload = JsonSerializer.Deserialize<LocalityFixtureFile>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Unable to deserialize locality fixture definitions.");
        return payload.Fixtures;
    }

    public static LocalityGridFixture LoadGridFixture(LocalityFixtureDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Grid))
        {
            throw new InvalidOperationException($"Fixture '{definition.Id}' does not specify a grid definition.");
        }

        var root = TestResourceLocator.RepositoryRoot;
        var path = Path.Combine(root, "LiteDB.Spatial.Core.Tests", "fixtures", definition.Grid);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LocalityGridFixture>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize grid fixture '{definition.Grid}'.");
    }

    public static LocalityAnalysis Analyze(LocalityFixtureDefinition definition, LocalityGridFixture grid)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        if (grid.Dimensions <= 0)
        {
            throw new InvalidOperationException("Grid fixtures must declare at least one dimension.");
        }

        if (grid.Shape.Length != grid.Dimensions)
        {
            throw new InvalidOperationException($"Grid '{grid.Id}' declares {grid.Dimensions} dimensions but provided {grid.Shape.Length} shape entries.");
        }

        var points = GeneratePoints(grid);
        var orderedByCode = points
            .OrderBy(p => p.MortonCode)
            .ThenBy(p => p.Index)
            .ToList();

        for (var i = 0; i < orderedByCode.Count; i++)
        {
            orderedByCode[i].MortonRank = i;
        }

        var neighbors = ComputeNearestNeighborRanks(points, definition.NeighborCount);
        var windowSizes = new List<int>(points.Count);
        var overlapSum = 0d;
        var minOverlap = double.MaxValue;
        var spanSum = 0d;
        var worstSpan = 0d;

        foreach (var point in points)
        {
            var ranks = neighbors[point.Index];
            var windowLow = Math.Max(0, point.MortonRank - definition.WindowRadius);
            var windowHigh = Math.Min(points.Count - 1, point.MortonRank + definition.WindowRadius);
            var windowSize = windowHigh - windowLow + 1;
            windowSizes.Add(windowSize);

            if (ranks.Count > 0)
            {
                var overlapCount = ranks.Count(rank => rank >= windowLow && rank <= windowHigh);
                var overlap = overlapCount / (double)definition.NeighborCount;
                overlapSum += overlap;
                minOverlap = Math.Min(minOverlap, overlap);

                var minRank = ranks[0];
                var maxRank = ranks[^1];
                var span = maxRank - minRank + 1;
                spanSum += span;
                worstSpan = Math.Max(worstSpan, span);
            }
            else
            {
                overlapSum += 1d;
                minOverlap = Math.Min(minOverlap, 1d);
            }
        }

        var metrics = new LocalityMetrics(
            AverageOverlap: overlapSum / points.Count,
            MinimumOverlap: minOverlap,
            AverageWindow: windowSizes.Average(),
            MaximumWindow: windowSizes.Max(),
            AverageSpan: points.Count == 0 ? 0d : spanSum / points.Count,
            WorstSpan: worstSpan);

        var duplicates = orderedByCode
            .GroupBy(p => p.MortonCode)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        return new LocalityAnalysis(
            definition,
            grid,
            orderedByCode,
            metrics,
            neighbors,
            windowSizes,
            duplicates);
    }

    private static List<LocalityPoint> GeneratePoints(LocalityGridFixture grid)
    {
        var encoder = new MortonIndexEncoder(grid.Dimensions, grid.PrecisionBits);
        var counts = grid.Shape;
        var total = 1;
        foreach (var count in counts)
        {
            if (count <= 0)
            {
                throw new InvalidOperationException("Grid dimensions must be positive.");
            }

            total = checked(total * count);
        }

        var result = new List<LocalityPoint>(total);
        var span = new double[grid.Dimensions];
        for (var axis = 0; axis < grid.Dimensions; axis++)
        {
            var range = grid.Max[axis] - grid.Min[axis];
            span[axis] = counts[axis] <= 1 ? 0d : range / (counts[axis] - 1);
        }

        for (var index = 0; index < total; index++)
        {
            var coordinates = new double[grid.Dimensions];
            var normalized = new double[grid.Dimensions];
            var remainder = index;

            for (var axis = grid.Dimensions - 1; axis >= 0; axis--)
            {
                var count = counts[axis];
                var position = remainder % count;
                remainder /= count;

                coordinates[axis] = count <= 1
                    ? grid.Min[axis]
                    : grid.Min[axis] + position * span[axis];

                var range = grid.Max[axis] - grid.Min[axis];
                normalized[axis] = range <= 0d
                    ? 0d
                    : (coordinates[axis] - grid.Min[axis]) / range;
            }

            var code = encoder.Encode(normalized);
            result.Add(new LocalityPoint(index, coordinates, code));
        }

        return result;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> ComputeNearestNeighborRanks(IReadOnlyList<LocalityPoint> points, int neighborCount)
    {
        var map = new Dictionary<int, IReadOnlyList<int>>(points.Count);
        var byIndex = points.ToDictionary(point => point.Index);

        foreach (var point in points)
        {
            var distances = new List<(int index, double distance)>(points.Count - 1);
            foreach (var candidate in points)
            {
                if (candidate.Index == point.Index)
                {
                    continue;
                }

                var distance = CalculateDistance(point.Coordinates, candidate.Coordinates);
                distances.Add((candidate.Index, distance));
            }

            distances.Sort((left, right) =>
            {
                var comparison = left.distance.CompareTo(right.distance);
                return comparison != 0 ? comparison : left.index.CompareTo(right.index);
            });

            var selected = distances
                .Take(neighborCount)
                .Select(item => byIndex[item.index].MortonRank)
                .OrderBy(rank => rank)
                .ToList();

            map[point.Index] = selected;
        }

        return map;
    }

    private static double CalculateDistance(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var sum = 0d;
        for (var axis = 0; axis < left.Count; axis++)
        {
            var delta = left[axis] - right[axis];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }

    private sealed class LocalityFixtureFile
    {
        public List<LocalityFixtureDefinition> Fixtures { get; set; } = new();
    }
}

public sealed class LocalityFixtureDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Grid { get; set; } = string.Empty;

    public int NeighborCount { get; set; }
        = 0;

    public int WindowRadius { get; set; }
        = 0;

    public int TopK { get; set; }
        = 0;

    public LocalityExpectation Expected { get; set; } = new();

    public LocalityMetrics ExpectedMetrics => new(
        Expected.AverageOverlap,
        Expected.MinimumOverlap,
        Expected.AverageWindow,
        Expected.MaximumWindow,
        Expected.AverageSpan,
        Expected.WorstSpan);
}

public sealed class LocalityExpectation
{
    public double AverageOverlap { get; set; }
        = 0d;

    public double MinimumOverlap { get; set; }
        = 0d;

    public double AverageWindow { get; set; }
        = 0d;

    public double MaximumWindow { get; set; }
        = 0d;

    public double AverageSpan { get; set; }
        = 0d;

    public double WorstSpan { get; set; }
        = 0d;

    public double MetricTolerance { get; set; }
        = 1e-6;
}

internal sealed class LocalityGridFixture
{
    public string Id { get; set; } = string.Empty;

    public int Dimensions { get; set; }
        = 0;

    public int[] Shape { get; set; } = Array.Empty<int>();

    public double[] Min { get; set; } = Array.Empty<double>();

    public double[] Max { get; set; } = Array.Empty<double>();

    public int PrecisionBits { get; set; }
        = 8;
}

internal sealed class LocalityPoint
{
    public LocalityPoint(int index, double[] coordinates, ulong mortonCode)
    {
        Index = index;
        Coordinates = coordinates;
        MortonCode = mortonCode;
    }

    public int Index { get; }

    public double[] Coordinates { get; }

    public ulong MortonCode { get; }

    public int MortonRank { get; set; }
        = -1;
}

internal sealed class LocalityAnalysis
{
    public LocalityAnalysis(
        LocalityFixtureDefinition definition,
        LocalityGridFixture grid,
        IReadOnlyList<LocalityPoint> orderedByCode,
        LocalityMetrics metrics,
        IReadOnlyDictionary<int, IReadOnlyList<int>> neighborRanks,
        IReadOnlyList<int> windowSizes,
        IReadOnlyList<ulong> duplicateCodes)
    {
        Definition = definition;
        Grid = grid;
        PointsByRank = orderedByCode;
        Metrics = metrics;
        NeighborRanks = neighborRanks;
        WindowSizes = windowSizes;
        DuplicateCodes = duplicateCodes;
    }

    public LocalityFixtureDefinition Definition { get; }

    public LocalityGridFixture Grid { get; }

    public IReadOnlyList<LocalityPoint> PointsByRank { get; }

    public LocalityMetrics Metrics { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<int>> NeighborRanks { get; }

    public IReadOnlyList<int> WindowSizes { get; }

    public IReadOnlyList<ulong> DuplicateCodes { get; }
}
