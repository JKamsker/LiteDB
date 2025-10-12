using System;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;

#nullable enable

namespace LiteDB.Spatial.Core.Tests.Support;

public static class SpatialTestAssertions
{
    public static void ShouldMatchFixture(this LocalityMetricsResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        result.ComputedCodes.Should().Equal(result.Fixture.Codes, "Morton encodings should remain stable for synthetic fixtures");
        result.Overlap.EffectiveTopK.Should().BeGreaterThan(0, "fixtures request meaningful nearest-neighbour comparisons");
        if (result.Fixture.BaselineAverage > 0)
        {
            result.AverageOverlap.Should().BeApproximately(result.Fixture.BaselineAverage, 0.01, "Morton locality should remain consistent with the recorded fixture baseline");
        }

        result.AverageOverlap.Should().BeGreaterOrEqualTo(result.Fixture.MinimumOverlap, "average locality should stay above the documented threshold");
        result.MinimumOverlap.Should().BeGreaterOrEqualTo(result.Fixture.MinimumOverlap * 0.75, "no individual point should regress far beyond the allowed average");
    }

    public static void ShouldDescribeIndexedPlan(this ExplainBreakdown breakdown, string? expectedExactPredicateFragment = null)
    {
        if (breakdown == null)
        {
            throw new ArgumentNullException(nameof(breakdown));
        }

        var result = breakdown.Result;
        result.RangeCount.Should().BeGreaterThan(0, "spatial queries should rely on Morton index ranges instead of full scans");
        result.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName, "spatial descriptors use the default _idx index field");
        result.BoundingBoxFieldName.Should().Be(SpatialIndexOptions.DefaultBoundingBoxFieldName, "spatial descriptors expose the coarse bounding box via _mbb");
        result.CoveringBounds.Should().NotBeNull("queries should pre-filter candidates using bounding boxes before executing exact predicates");

        breakdown.IndexFieldLine.Should().BeGreaterOrEqualTo(0, "explain output should contain an index field section");
        breakdown.IndexRangesLine.Should().BeGreaterThan(breakdown.IndexFieldLine, "index ranges should be listed after the field metadata");
        breakdown.CoveringBoundsLine.Should().BeGreaterThan(breakdown.IndexRangesLine, "covering bounds should be described after the ranges");
        breakdown.ExactPredicateLine.Should().BeGreaterThan(breakdown.CoveringBoundsLine, "exact predicate descriptions should follow index pre-filters");

        breakdown.Lines[breakdown.IndexRangesLine].Should().Contain(result.IndexFieldName!, "the explain summary should annotate the index field used for the scan");
        breakdown.Lines[breakdown.CoveringBoundsLine].Should().Contain(result.BoundingBoxFieldName!, "the explain summary should surface the bounding box prefilter");

        if (!string.IsNullOrWhiteSpace(expectedExactPredicateFragment) && result.ExactPredicate is not null)
        {
            result.ExactPredicate.Should().Contain(expectedExactPredicateFragment);
        }
    }
}
