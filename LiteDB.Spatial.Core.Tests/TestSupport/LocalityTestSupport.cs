using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests;

internal static class LocalityTestSupport
{
    public static IReadOnlyList<double[]> GenerateGrid(IReadOnlyList<int> shape)
    {
        if (shape == null || shape.Count == 0)
        {
            throw new ArgumentException("Grid shape must contain at least one dimension.", nameof(shape));
        }

        var totalPoints = shape.Aggregate(1, (product, value) => product * value);
        var dimensions = shape.Count;
        var points = new double[totalPoints][];

        var indices = new int[dimensions];
        var cursor = 0;

        void Recurse(int axis)
        {
            if (axis == dimensions)
            {
                var coordinates = new double[dimensions];
                for (var i = 0; i < dimensions; i++)
                {
                    var steps = shape[i] - 1;
                    coordinates[i] = steps == 0 ? 0d : indices[i] / (double)steps;
                }

                points[cursor++] = coordinates;
                return;
            }

            for (var i = 0; i < shape[axis]; i++)
            {
                indices[axis] = i;
                Recurse(axis + 1);
            }
        }

        Recurse(0);
        return points;
    }

    public static IReadOnlyList<ulong> EncodePoints(IReadOnlyList<double[]> points, MortonIndexEncoder encoder, int dimensions)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        var codes = new ulong[points.Count];
        Span<double> buffer = dimensions <= 3 ? stackalloc double[dimensions] : new double[dimensions];

        for (var i = 0; i < points.Count; i++)
        {
            var coordinates = points[i];
            for (var axis = 0; axis < dimensions; axis++)
            {
                buffer[axis] = coordinates[axis];
            }

            codes[i] = encoder.Encode(buffer);
        }

        return codes;
    }

    public static LocalityMetrics ComputeMetrics(
        IReadOnlyList<double[]> points,
        IReadOnlyList<ulong> codes,
        int dimensions,
        int neighborCount,
        int windowRadius)
    {
        if (points.Count != codes.Count)
        {
            throw new ArgumentException("The number of points must match the number of encoded codes.");
        }

        var totalPoints = points.Count;
        var sortedIndices = Enumerable.Range(0, totalPoints).OrderBy(i => codes[i]).ToArray();
        var inverseIndex = new int[totalPoints];
        for (var i = 0; i < sortedIndices.Length; i++)
        {
            inverseIndex[sortedIndices[i]] = i;
        }

        var neighborWork = new List<(double distance, int index)>(totalPoints - 1);
        var windowNeighbors = new HashSet<int>();

        double overlapSum = 0d;
        double windowSum = 0d;

        for (var pointIndex = 0; pointIndex < totalPoints; pointIndex++)
        {
            neighborWork.Clear();
            windowNeighbors.Clear();

            var point = points[pointIndex];

            for (var otherIndex = 0; otherIndex < totalPoints; otherIndex++)
            {
                if (otherIndex == pointIndex)
                {
                    continue;
                }

                var other = points[otherIndex];
                var distance = EuclideanDistance(point, other, dimensions);
                neighborWork.Add((distance, otherIndex));
            }

            neighborWork.Sort((left, right) => left.distance.CompareTo(right.distance));
            var exactNeighbors = neighborWork.Take(Math.Min(neighborCount, neighborWork.Count)).Select(x => x.index).ToHashSet();

            var sortedPosition = inverseIndex[pointIndex];
            var start = Math.Max(0, sortedPosition - windowRadius);
            var end = Math.Min(sortedIndices.Length - 1, sortedPosition + windowRadius);

            for (var i = start; i <= end; i++)
            {
                var candidate = sortedIndices[i];
                if (candidate == pointIndex)
                {
                    continue;
                }

                windowNeighbors.Add(candidate);
            }

            var overlap = exactNeighbors.Intersect(windowNeighbors).Count();
            overlapSum += overlap / (double)Math.Max(1, neighborCount);
            windowSum += windowNeighbors.Count;
        }

        var averageOverlap = overlapSum / totalPoints;
        var averageWindow = windowSum / totalPoints;
        return new LocalityMetrics(averageOverlap, averageWindow);
    }

    private static double EuclideanDistance(IReadOnlyList<double> left, IReadOnlyList<double> right, int dimensions)
    {
        var sum = 0d;
        for (var axis = 0; axis < dimensions; axis++)
        {
            var delta = left[axis] - right[axis];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }
}

internal readonly record struct LocalityMetrics(double AverageOverlap, double AverageWindow);
