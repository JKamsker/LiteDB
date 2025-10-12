using System;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Diagnostics;

public sealed class SpatialDiagnosticsTests
{
    [Fact]
    public void ExplainReflectsPlanDetails()
    {
        var ranges = new[]
        {
            new SpatialIndexRange(1, 5),
            new SpatialIndexRange(6, 10)
        };
        var covering = new SpatialCoveringResult(ranges, estimatedCellCount: 10, requestedMaxCells: 16, originalRangeCount: ranges.Length);
        var plan = new SpatialQueryPlan(
            "Geographic2D",
            2,
            BoundingBox.From2D(-1, -2, 3, 4),
            covering,
            "Distance <= 10");

        var descriptor = new SpatialCollectionDescriptor("places", "Geographic2D", 2, "location", new SpatialIndexOptions());
        var explain = SpatialDiagnostics.Explain(plan, descriptor);

        explain.EngineName.Should().Be("Geographic2D");
        explain.Dimensions.Should().Be(2);
        explain.CoveringBounds.Should().Be(plan.CoveringBounds);
        explain.IndexRanges.Should().ContainInOrder(plan.IndexRanges);
        explain.RangeCount.Should().Be(2);
        explain.ExactPredicate.Should().Be("Distance <= 10");
        explain.IndexFieldName.Should().Be(descriptor.Options.IndexFieldName);
        explain.BoundingBoxFieldName.Should().Be(descriptor.Options.BoundingBoxFieldName);

        var summary = explain.ToString();
        summary.Should().Contain("Engine: Geographic2D (2D)");
        summary.Should().Contain("Index field: _idx");
        summary.Should().Contain("Bounding box field: _mbb");
        summary.Should().Contain("Index ranges (2) via _idx");
        summary.Should().Contain("[1, 5] (0x0000000000000001 - 0x0000000000000005)");
        summary.Should().Contain("Covering bounds via _mbb");
        summary.Should().Contain("Covering ranges: 2 / 16");
        summary.Should().Contain("Estimated covering cells: 10");
        summary.Should().Contain("Original covering ranges: 2");
        summary.Should().Contain("Exact predicate: Distance <= 10");
    }

    [Fact]
    public void ExplainHandlesEmptyPlan()
    {
        var covering = new SpatialCoveringResult(Array.Empty<SpatialIndexRange>(), 0, requestedMaxCells: 1, originalRangeCount: 0);
        var plan = new SpatialQueryPlan(
            "Cartesian3D",
            3,
            null,
            covering,
            null);

        var explain = SpatialDiagnostics.Explain(plan);

        explain.EngineName.Should().Be("Cartesian3D");
        explain.Dimensions.Should().Be(3);
        explain.CoveringBounds.Should().BeNull();
        explain.IndexRanges.Should().BeEmpty();
        explain.ExactPredicate.Should().BeNull();

        var summary = explain.ToString();
        summary.Should().Contain("Index field: n/a");
        summary.Should().Contain("Bounding box field: n/a");
        summary.Should().Contain("Index ranges (0)");
        summary.Should().Contain("Covering ranges: 0 / 1");
        summary.Should().Contain("Estimated covering cells: 0");
        summary.Should().Contain("Original covering ranges: 0");
        summary.Should().Contain("Covering bounds: none");
        summary.Should().Contain("Exact predicate: none");
    }
}
