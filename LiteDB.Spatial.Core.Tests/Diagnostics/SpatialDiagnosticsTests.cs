using System;
using System.Collections.Generic;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Diagnostics;

public sealed class SpatialDiagnosticsTests
{
    [Fact]
    public void ExplainProducesSummary()
    {
        var options = new SpatialIndexOptions(precisionBits: 4, maxCoveringCells: 8, distanceTolerance: 0.5);
        var descriptor = new SpatialCollectionDescriptor("places", "Geographic2D", 2, "Location", options);
        var ranges = new List<SpatialIndexRange>
        {
            new SpatialIndexRange(10, 12),
            new SpatialIndexRange(20, 25)
        };

        var plan = new SpatialQueryPlan(2, BoundingBox.From2D(-1, -1, 1, 1), ranges, "Haversine <= 500 m");

        var result = SpatialDiagnostics.Explain(descriptor, plan);

        result.CollectionName.Should().Be("places");
        result.EngineName.Should().Be("Geographic2D");
        result.Dimensions.Should().Be(2);
        result.IndexRanges.Should().BeEquivalentTo(ranges, options => options.WithStrictOrdering());
        result.CoveringBounds.Should().Be(plan.CoveringBounds);
        result.ExactPredicate.Should().Be("Haversine <= 500 m");

        var summary = result.ToString();
        summary.Should().Contain("Collection : places");
        summary.Should().Contain("Engine     : Geographic2D (2D)");
        summary.Should().Contain("Ranges (2):");
        summary.Should().Contain("[10, 12]");
        summary.Should().Contain("Predicate  : Haversine <= 500 m");
    }

    [Fact]
    public void ExplainRequiresArguments()
    {
        var options = new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor("places", "Geographic2D", 2, "Location", options);

        Assert.Throws<System.ArgumentNullException>(() => SpatialDiagnostics.Explain(descriptor, null!));
        Assert.Throws<System.ArgumentNullException>(() => SpatialDiagnostics.Explain(null!, new SpatialQueryPlan(2, null, Array.Empty<SpatialIndexRange>(), null)));
    }
}

