using System;
using FluentAssertions;
using FluentAssertions.Execution;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class LocalityAssertions
{
    public static void AssertLocality(LocalityAnalysis analysis)
    {
        if (analysis == null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        using var scope = new AssertionScope();
        scope.AddReportable("fixture", analysis.Definition.Id);
        scope.AddReportable("points", analysis.PointsByRank.Count.ToString());
        scope.AddReportable("neighborCount", analysis.Definition.NeighborCount.ToString());
        scope.AddReportable("windowRadius", analysis.Definition.WindowRadius.ToString());
        scope.AddReportable("topK", analysis.Definition.TopK.ToString());

        var tolerance = analysis.Definition.Expected.MetricTolerance;
        var expected = analysis.Definition.ExpectedMetrics;
        var metrics = analysis.Metrics;

        metrics.AverageOverlap.Should().BeApproximately(expected.AverageOverlap, tolerance, "average overlap should remain within the recorded envelope");
        metrics.MinimumOverlap.Should().BeGreaterOrEqualTo(expected.MinimumOverlap - tolerance, "minimum overlap should not regress below the expected threshold");
        metrics.AverageWindow.Should().BeApproximately(expected.AverageWindow, tolerance, "average Morton window should remain stable");
        metrics.MaximumWindow.Should().BeLessOrEqualTo(expected.MaximumWindow + tolerance, "maximum Morton window should stay bounded");
        metrics.AverageSpan.Should().BeApproximately(expected.AverageSpan, tolerance, "average span should remain consistent");
        metrics.WorstSpan.Should().BeLessOrEqualTo(expected.WorstSpan + tolerance, "worst-case span should stay within the budget");

        analysis.DuplicateCodes.Should().BeEmpty("Morton codes should be unique for grid '{0}'", analysis.Grid.Id);

        analysis.Definition.TopK.Should().BeGreaterThan(0, "top-K neighbor windows must be configured");
        analysis.PointsByRank.Count.Should().BeGreaterThan(analysis.Definition.TopK, "fixtures should have enough points to evaluate top-K neighborhoods");

        SpatialTestAssertions.AssertUniqueMortonCodes(analysis.PointsByRank, analysis.Grid.Id);
        SpatialTestAssertions.AssertTopKNeighborhoods(analysis.PointsByRank, analysis.Definition.TopK);
    }
}
