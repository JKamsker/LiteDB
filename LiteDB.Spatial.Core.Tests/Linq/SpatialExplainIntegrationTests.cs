using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialExplainIntegrationTests
{
    [Fact]
    public void NearExplainHighlightsIndexPredicates()
    {
        var options = new SpatialIndexOptions(precisionBits: 10);
        var engine = new Cartesian2DEngine("geometry", BoundingBox.From2D(0, 0, 1, 1), options);
        var descriptor = new SpatialCollectionDescriptor("locality", engine.Name, 2, "geometry", options).WithEngine(engine);

        var plan = engine.PlanNear(new GeoPoint(0.5, 0.5), radius: 0.2);
        var explain = SpatialDiagnostics.Explain(plan, descriptor);

        explain.ShouldHighlightIndexPrefilters();
        var layout = explain.AnalyzeLayout();
        layout.CoveringBoundsLine.Should().BeGreaterOrEqualTo(0);
        layout.CoveringBoundsLine.Should().BeLessThan(layout.ExactPredicateLine);
    }

    [Fact]
    public void WithinExplainKeepsBoundingChecksAheadOfExactFilters()
    {
        var options = new SpatialIndexOptions(precisionBits: 9);
        var engine = new Cartesian3DEngine("geometry", BoundingBox.From3D(0, 0, 0, 1, 1, 1), options);
        var descriptor = new SpatialCollectionDescriptor("locality3d", engine.Name, 3, "geometry", options).WithEngine(engine);

        var bounds = BoundingBox.From3D(0.1, 0.2, 0.3, 0.6, 0.7, 0.8);
        var plan = engine.PlanWithin(bounds);
        var explain = SpatialDiagnostics.Explain(plan, descriptor);

        explain.ShouldHighlightIndexPrefilters();
        explain.CoveringBounds.Should().Be(bounds);
    }
}
