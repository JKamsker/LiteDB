using System;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public static class ExplainPlanAssertions
{
    public static void AssertIndexedPlan(SpatialExplainResult explain)
    {
        explain.Should().NotBeNull();
        explain.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName);
        explain.BoundingBoxFieldName.Should().Be(SpatialIndexOptions.DefaultBoundingBoxFieldName);
        explain.RangeCount.Should().BeGreaterThan(0, "indexed plans must scan at least one Morton range");
        explain.CoveringBounds.Should().NotBeNull("prefilters must be applied before exact distance checks");
        explain.ExactPredicate.Should().NotBeNullOrWhiteSpace();

        var summary = explain.ToString();
        var lines = summary.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var indexLine = Array.FindIndex(lines, line => line.Contains("Index ranges", StringComparison.OrdinalIgnoreCase));
        var boundingLine = Array.FindIndex(lines, line => line.Contains("Covering bounds", StringComparison.OrdinalIgnoreCase));
        var exactLine = Array.FindIndex(lines, line => line.Contains("Exact predicate", StringComparison.OrdinalIgnoreCase));

        indexLine.Should().BeGreaterOrEqualTo(0, "explain output should include index range details");
        boundingLine.Should().BeGreaterOrEqualTo(0, "explain output should include bounding box filtering details");
        exactLine.Should().BeGreaterOrEqualTo(0, "explain output should include the exact predicate");

        indexLine.Should().BeLessThan(boundingLine, "index ranges must be applied before bounding box checks");
        boundingLine.Should().BeLessThan(exactLine, "covering bounds should run before the exact predicate");

        summary.Should().Contain("_idx", "the default index field should be referenced");
        summary.Should().Contain("_mbb", "the default bounding box field should be referenced");
        summary.Should().NotContain("Index ranges (0)", "full scans should not be reported for indexed plans");
    }
}
