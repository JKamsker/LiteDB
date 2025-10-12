using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public static class LocalityTestHelpers
{
    public static double[][] GenerateGrid(int dimensions, int gridSize)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        }

        if (gridSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize));
        }

        var total = (int)Math.Pow(gridSize, dimensions);
        var result = new double[total][];
        var step = gridSize - 1;

        for (var index = 0; index < total; index++)
        {
            var point = new double[dimensions];
            var value = index;

            for (var axis = 0; axis < dimensions; axis++)
            {
                var coordinate = value % gridSize;
                point[axis] = step == 0 ? 0d : coordinate / (double)step;
                value /= gridSize;
            }

            result[index] = point;
        }

        return result;
    }

    public static LocalityMetrics ComputeMetrics(IReadOnlyList<double[]> points, IReadOnlyList<ulong> codes, int neighborCount)
    {
        if (points.Count != codes.Count)
        {
            throw new ArgumentException("Point and code counts must match.");
        }

        if (neighborCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(neighborCount));
        }

        var ordered = points
            .Select((point, index) => new OrderedPoint(point, codes[index], index))
            .OrderBy(entry => entry.Code)
            .ThenBy(entry => entry.OriginalIndex)
            .ToArray();

        var positionByOriginalIndex = new int[points.Count];
        for (var i = 0; i < ordered.Length; i++)
        {
            positionByOriginalIndex[ordered[i].OriginalIndex] = i;
        }

        var totalOverlap = 0d;
        var totalSpan = 0d;
        var worstSpan = 0d;

        for (var i = 0; i < points.Count; i++)
        {
            var neighbors = FindNearestNeighbors(points, i, neighborCount);
            var position = positionByOriginalIndex[i];
            var windowStart = Math.Max(0, position - neighborCount);
            var windowEnd = Math.Min(ordered.Length - 1, position + neighborCount);
            var span = windowEnd - windowStart;
            totalSpan += span;
            worstSpan = Math.Max(worstSpan, span);

            var windowSet = new HashSet<int>();
            for (var j = windowStart; j <= windowEnd; j++)
            {
                windowSet.Add(ordered[j].OriginalIndex);
            }

            var overlap = neighbors.Count(windowSet.Contains);
            totalOverlap += overlap / (double)neighborCount;
        }

        return new LocalityMetrics(
            AverageOverlap: totalOverlap / points.Count,
            AverageSpan: totalSpan / points.Count,
            WorstCaseSpan: worstSpan);
    }

    private static IReadOnlyList<int> FindNearestNeighbors(IReadOnlyList<double[]> points, int originIndex, int count)
    {
        var origin = points[originIndex];
        return Enumerable.Range(0, points.Count)
            .Where(i => i != originIndex)
            .Select(i => (Index: i, Distance: EuclideanDistance(origin, points[i])))
            .OrderBy(pair => pair.Distance)
            .Take(count)
            .Select(pair => pair.Index)
            .ToArray();
    }

    private static double EuclideanDistance(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var total = 0d;
        for (var axis = 0; axis < left.Count; axis++)
        {
            var delta = left[axis] - right[axis];
            total += delta * delta;
        }

        return Math.Sqrt(total);
    }

    private readonly record struct OrderedPoint(double[] Point, ulong Code, int OriginalIndex);
}
