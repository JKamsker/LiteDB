#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class LocalityMetrics
{
    public static LocalityReport EvaluateLocality(
        MortonLocalityCase @case,
        IReadOnlyList<double[]> points,
        IReadOnlyList<ulong> codes)
    {
        if (points.Count != codes.Count)
        {
            throw new ArgumentException("Point and code counts must match.");
        }

        var neighborCount = @case.NeighborCount;
        var windowRadius = @case.MortonWindowRadius;
        var overlaps = new double[points.Count];

        var order = codes
            .Select((code, index) => (code, index))
            .OrderBy(tuple => tuple.code)
            .Select(tuple => tuple.index)
            .ToArray();

        var positions = new int[order.Length];
        for (var position = 0; position < order.Length; position++)
        {
            positions[order[position]] = position;
        }

        var neighborSets = BuildNeighborSets(points, neighborCount);

        for (var index = 0; index < points.Count; index++)
        {
            var position = positions[index];
            var start = Math.Max(0, position - windowRadius);
            var end = Math.Min(order.Length - 1, position + windowRadius);
            var window = new HashSet<int>();

            for (var cursor = start; cursor <= end; cursor++)
            {
                var candidate = order[cursor];
                if (candidate != index)
                {
                    window.Add(candidate);
                }
            }

            var actualNeighbors = neighborSets[index];
            var overlap = actualNeighbors.Count == 0
                ? 0d
                : window.Intersect(actualNeighbors).Count() / (double)actualNeighbors.Count;
            overlaps[index] = overlap;
        }

        var average = overlaps.Average();
        var minimum = overlaps.Min();
        return new LocalityReport(average, minimum);
    }

    public static CandidateReductionReport MeasureCandidateReduction(
        IReadOnlyList<SpatialIndexRange> ranges,
        IReadOnlyList<ulong> codes)
    {
        var total = codes.Count;
        if (total == 0)
        {
            return new CandidateReductionReport(0, 0);
        }

        var hits = 0;
        foreach (var code in codes)
        {
            foreach (var range in ranges)
            {
                if (code >= range.Start && code <= range.End)
                {
                    hits++;
                    break;
                }
            }
        }

        return new CandidateReductionReport(total, hits);
    }

    public static void AssertMeetsTargets(MortonLocalityCase @case, LocalityReport report)
    {
        report.AverageOverlap.Should().BeGreaterOrEqualTo(@case.TargetAverageOverlap, $"fixture '{@case.Name}' should retain the target average neighbor overlap");
        report.MinimumOverlap.Should().BeGreaterOrEqualTo(@case.TargetMinimumOverlap, $"fixture '{@case.Name}' should retain the target worst-case neighbor overlap");
    }

    private static HashSet<int>[] BuildNeighborSets(IReadOnlyList<double[]> points, int neighborCount)
    {
        var result = new HashSet<int>[points.Count];

        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var distances = new List<(double Distance, int Index)>(points.Count - 1);

            for (var candidate = 0; candidate < points.Count; candidate++)
            {
                if (candidate == index)
                {
                    continue;
                }

                var distance = Distance(current, points[candidate]);
                distances.Add((distance, candidate));
            }

            distances.Sort((left, right) => left.Distance.CompareTo(right.Distance));
            var neighbors = distances.Take(Math.Min(neighborCount, distances.Count)).Select(tuple => tuple.Index);
            result[index] = new HashSet<int>(neighbors);
        }

        return result;
    }

    private static double Distance(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count)
        {
            throw new ArgumentException("Points must have the same dimensionality.");
        }

        var sum = 0d;
        for (var axis = 0; axis < left.Count; axis++)
        {
            var delta = left[axis] - right[axis];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }
}

internal readonly record struct LocalityReport(double AverageOverlap, double MinimumOverlap);

internal readonly record struct CandidateReductionReport(int TotalCandidates, int IndexCandidates)
{
    public double ReductionRatio => TotalCandidates == 0 ? 0d : 1d - (IndexCandidates / (double)TotalCandidates);
}
