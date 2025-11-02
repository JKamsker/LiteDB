using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class SpatialTestAssertions
{
    public static void AssertUniqueMortonCodes(IEnumerable<LocalityPoint> points, string context)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        points.Select(point => point.MortonCode)
            .Should()
            .OnlyHaveUniqueItems($"Morton codes should remain unique for fixture '{context}'");
    }

    public static void AssertTopKNeighborhoods(IReadOnlyList<LocalityPoint> ordered, int topK)
    {
        if (ordered == null)
        {
            throw new ArgumentNullException(nameof(ordered));
        }

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "Top-K window must be positive.");
        }

        if (ordered.Count <= topK)
        {
            throw new ArgumentException("Top-K windows require at least topK + 1 points to evaluate.", nameof(ordered));
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var neighbors = CollectNeighbors(ordered, i, topK);
            neighbors.Count.Should().Be(topK, $"point at Morton rank {i} should expose exactly {topK} neighbors");
            neighbors.Select(point => point.MortonCode).Should().OnlyHaveUniqueItems("neighbor Morton codes should remain unique");
        }
    }

    private static IReadOnlyList<LocalityPoint> CollectNeighbors(IReadOnlyList<LocalityPoint> ordered, int rank, int topK)
    {
        var neighbors = new List<LocalityPoint>(topK);
        var left = rank - 1;
        var right = rank + 1;

        while (neighbors.Count < topK && (left >= 0 || right < ordered.Count))
        {
            if (left >= 0)
            {
                neighbors.Add(ordered[left]);
                left--;
                if (neighbors.Count == topK)
                {
                    break;
                }
            }

            if (right < ordered.Count)
            {
                neighbors.Add(ordered[right]);
                right++;
            }
        }

        return neighbors;
    }
}
