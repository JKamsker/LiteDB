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
        var plan = new SpatialQueryPlan(
            "Geographic2D",
            2,
            BoundingBox.From2D(-1, -2, 3, 4),
            new[]
            {
                new SpatialIndexRange(1, 5),
                new SpatialIndexRange(6, 10)
            },
            "Distance <= 10");

        var explain = SpatialDiagnostics.Explain(plan);

        explain.EngineName.Should().Be("Geographic2D");
        explain.Dimensions.Should().Be(2);
        explain.CoveringBounds.Should().Be(plan.CoveringBounds);
        explain.IndexRanges.Should().ContainInOrder(plan.IndexRanges);
        explain.RangeCount.Should().Be(2);
        explain.ExactPredicate.Should().Be("Distance <= 10");

        var summary = explain.ToString();
        summary.Should().Contain("Engine: Geographic2D (2D)");
        summary.Should().Contain("Index ranges (2)");
        summary.Should().Contain("[1, 5]");
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
            null);

        var explain = SpatialDiagnostics.Explain(plan);

        explain.EngineName.Should().Be("Cartesian3D");
        explain.Dimensions.Should().Be(3);
        explain.CoveringBounds.Should().BeNull();
        explain.IndexRanges.Should().BeEmpty();
        explain.ExactPredicate.Should().BeNull();

        var summary = explain.ToString();
        summary.Should().Contain("Index ranges (0)");
        summary.Should().Contain("Covering bounds: none");
        summary.Should().Contain("Exact predicate: none");
    }
}
