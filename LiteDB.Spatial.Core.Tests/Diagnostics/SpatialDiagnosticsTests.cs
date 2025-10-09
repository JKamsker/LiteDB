#nullable enable

using System;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Diagnostics;

public sealed class SpatialDiagnosticsTests
{
    [Fact]
    public void ExplainReturnsResultWithMetadata()
    {
        var options = new SpatialIndexOptions(precisionBits: 9, maxCoveringCells: 32, distanceTolerance: 2);
        var descriptor = new SpatialCollectionDescriptor(
            collectionName: "places",
            engineName: GeographicEngine.EngineName,
            dimensions: 2,
            geometryFieldName: "location",
            options: options,
            settings: SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));

        var ranges = new[]
        {
            new SpatialIndexRange(10, 20),
            new SpatialIndexRange(30, 40)
        };
        var plan = new SpatialQueryPlan(2, BoundingBox.From2D(-10, -10, 10, 10), ranges, "Haversine <= 1000 m");

        var result = SpatialDiagnostics.Explain(descriptor, plan);

        result.EngineName.Should().Be(GeographicEngine.EngineName);
        result.Dimensions.Should().Be(2);
        result.IndexRangeCount.Should().Be(2);
        result.CoveringBounds.Should().Be(plan.CoveringBounds);
        result.IndexFieldName.Should().Be(options.IndexFieldName);
        result.BoundingBoxFieldName.Should().Be(options.BoundingBoxFieldName);
        result.ExactPredicate.Should().Be(plan.ExactPredicateDescription);

        var summary = result.ToString();
        summary.Should().Contain("Engine:");
        summary.Should().Contain("Index ranges (2)");
        summary.Should().Contain("Exact predicate");
    }

    [Fact]
    public void ExplainRequiresArguments()
    {
        var options = new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor("c", GeographicEngine.EngineName, 2, "loc", options);
        var plan = new SpatialQueryPlan(2, null, new[] { new SpatialIndexRange(1, 2) }, null);

        Assert.Throws<ArgumentNullException>(() => SpatialDiagnostics.Explain(null!, plan));
        Assert.Throws<ArgumentNullException>(() => SpatialDiagnostics.Explain(descriptor, null!));
    }
}
