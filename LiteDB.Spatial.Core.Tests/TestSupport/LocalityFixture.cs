using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Support;

public sealed class LocalityFixture
{
    public string Id { get; init; } = string.Empty;

    public int Dimensions { get; init; }

    public int PrecisionBits { get; init; }

    public int[] GridShape { get; init; } = Array.Empty<int>();

    public int TopK { get; init; }

    public double[][] Coordinates { get; init; } = Array.Empty<double[]>();

    public ulong[] Codes { get; init; } = Array.Empty<ulong>();

    public double MinimumOverlap { get; init; }

    public double BaselineAverage { get; init; }

    public static LocalityFixture Load(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Fixture identifier must be provided.", nameof(id));
        }

        var fileName = id.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? id : id + ".json";
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Locality fixture '{id}' could not be found. Copy fixtures to the test output directory using the csproj CopyToOutputDirectory metadata.", path);
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var fixture = JsonSerializer.Deserialize<LocalityFixture>(json, options);
        if (fixture is null)
        {
            throw new InvalidOperationException($"Failed to deserialize locality fixture '{id}'.");
        }

        return fixture;
    }

    public LocalityMetricsResult Evaluate()
    {
        var encoder = new MortonIndexEncoder(Dimensions, PrecisionBits);
        return Evaluate(encoder);
    }

    public LocalityMetricsResult Evaluate(MortonIndexEncoder encoder)
    {
        if (encoder is null)
        {
            throw new ArgumentNullException(nameof(encoder));
        }

        if (encoder.Dimensions != Dimensions)
        {
            throw new ArgumentException($"Encoder dimensions ({encoder.Dimensions}) must match fixture dimensions ({Dimensions}).", nameof(encoder));
        }

        if (encoder.PrecisionBits != PrecisionBits)
        {
            throw new ArgumentException($"Encoder precision ({encoder.PrecisionBits}) must match fixture precision ({PrecisionBits}).", nameof(encoder));
        }

        var count = Coordinates.Length;
        var computed = new ulong[count];
        for (var i = 0; i < count; i++)
        {
            computed[i] = encoder.Encode(Coordinates[i]);
        }

        var overlaps = LocalityMetrics.ComputeOverlaps(Coordinates, computed, TopK);
        return new LocalityMetricsResult(this, computed, overlaps);
    }
}

public static class LocalityMetrics
{
    public static LocalityOverlap ComputeOverlaps(IReadOnlyList<double[]> coordinates, IReadOnlyList<ulong> codes, int requestedTopK)
    {
        if (coordinates == null)
        {
            throw new ArgumentNullException(nameof(coordinates));
        }

        if (codes == null)
        {
            throw new ArgumentNullException(nameof(codes));
        }

        if (coordinates.Count != codes.Count)
        {
            throw new ArgumentException("Coordinate and code collections must have the same length.");
        }

        var count = coordinates.Count;
        var effectiveTopK = Math.Min(Math.Max(requestedTopK, 0), Math.Max(0, count - 1));

        var ordered = codes
            .Select((code, index) => new MortonOrdering(code, index))
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Index)
            .ToArray();

        var positions = new int[count];
        for (var i = 0; i < ordered.Length; i++)
        {
            positions[ordered[i].Index] = i;
        }

        var overlaps = new double[count];
        var mortonNeighborhoodSizes = new int[count];

        for (var i = 0; i < count; i++)
        {
            if (effectiveTopK == 0)
            {
                overlaps[i] = 1d;
                mortonNeighborhoodSizes[i] = 0;
                continue;
            }

            var actualNeighbors = GetActualNeighbors(coordinates, i, effectiveTopK);
            var mortonNeighbors = GetMortonNeighbors(ordered, positions, i, effectiveTopK);
            mortonNeighborhoodSizes[i] = mortonNeighbors.Count;

            if (mortonNeighbors.Count == 0)
            {
                overlaps[i] = 0d;
                continue;
            }

            var shared = mortonNeighbors.Count(candidate => actualNeighbors.Contains(candidate));
            overlaps[i] = shared / (double)effectiveTopK;
        }

        return new LocalityOverlap(overlaps, mortonNeighborhoodSizes, effectiveTopK);
    }

    private static HashSet<int> GetActualNeighbors(IReadOnlyList<double[]> coordinates, int index, int topK)
    {
        var distances = new List<(int Index, double Distance)>(coordinates.Count - 1);
        var source = coordinates[index];

        for (var i = 0; i < coordinates.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            var distance = EuclideanDistanceSquared(source, coordinates[i]);
            distances.Add((i, distance));
        }

        distances.Sort((left, right) =>
        {
            var comparison = left.Distance.CompareTo(right.Distance);
            return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
        });

        var limit = Math.Min(topK, distances.Count);
        var set = new HashSet<int>();
        for (var i = 0; i < limit; i++)
        {
            set.Add(distances[i].Index);
        }

        return set;
    }

    private static List<int> GetMortonNeighbors(IReadOnlyList<MortonOrdering> ordered, IReadOnlyList<int> positions, int index, int topK)
    {
        var target = ordered[positions[index]];
        var limit = Math.Min(topK, ordered.Count - 1);

        return ordered
            .Where(candidate => candidate.Index != index)
            .Select(candidate => new
            {
                candidate.Index,
                Distance = Math.Abs(unchecked((long)candidate.Code - (long)target.Code))
            })
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Index)
            .Take(limit)
            .Select(candidate => candidate.Index)
            .ToList();
    }

    private static double EuclideanDistanceSquared(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var sum = 0d;
        for (var i = 0; i < left.Count; i++)
        {
            var delta = left[i] - right[i];
            sum += delta * delta;
        }

        return sum;
    }

    private readonly record struct MortonOrdering(ulong Code, int Index);
}

public sealed class LocalityMetricsResult
{
    public LocalityMetricsResult(LocalityFixture fixture, IReadOnlyList<ulong> computedCodes, LocalityOverlap overlap)
    {
        Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        ComputedCodes = computedCodes ?? throw new ArgumentNullException(nameof(computedCodes));
        Overlap = overlap ?? throw new ArgumentNullException(nameof(overlap));
    }

    public LocalityFixture Fixture { get; }

    public IReadOnlyList<ulong> ComputedCodes { get; }

    public LocalityOverlap Overlap { get; }

    public double AverageOverlap => Overlap.Values.Count == 0 ? 1d : Overlap.Values.Average();

    public double MinimumOverlap => Overlap.Values.Count == 0 ? 1d : Overlap.Values.Min();
}

public sealed class LocalityOverlap
{
    public LocalityOverlap(IReadOnlyList<double> values, IReadOnlyList<int> mortonNeighborhoodSizes, int effectiveTopK)
    {
        Values = values ?? throw new ArgumentNullException(nameof(values));
        MortonNeighborhoodSizes = mortonNeighborhoodSizes ?? throw new ArgumentNullException(nameof(mortonNeighborhoodSizes));
        EffectiveTopK = effectiveTopK;
    }

    public IReadOnlyList<double> Values { get; }

    public IReadOnlyList<int> MortonNeighborhoodSizes { get; }

    public int EffectiveTopK { get; }
}
