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
        var diagnostics = new SpatialCoveringDiagnostics(
            requestedRangeCount: 2,
            returnedRangeCount: 2,
            estimatedCellCount: 12,
            usedMaxCoveringCellsFallback: true,
            usedEnumerationFallback: false).WithEffectiveRangeCount(2);

        var plan = new SpatialQueryPlan(
            "Geographic2D",
            2,
            BoundingBox.From2D(-1, -2, 3, 4),
            new[]
            {
                new SpatialIndexRange(1, 5),
                new SpatialIndexRange(6, 10)
            },
            "Distance <= 10",
            diagnostics);

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
        summary.Should().Contain("Covering diagnostics: requested=2, returned=2, effective=2, estimated=12, maxFallback=true, enumerationFallback=false");
        summary.Should().Contain("Exact predicate: Distance <= 10");
    }

    [Fact]
    public void ExplainHandlesEmptyPlan()
    {
        var plan = new SpatialQueryPlan(
            "Cartesian3D",
            3,
            null,
            Array.Empty<SpatialIndexRange>(),
            null,
            SpatialCoveringDiagnostics.Empty);

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
        summary.Should().Contain("Covering diagnostics: requested=0, returned=0, effective=0, estimated=0, maxFallback=false, enumerationFallback=false");
        summary.Should().Contain("Exact predicate: none");
    }
}
