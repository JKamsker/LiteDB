#nullable enable

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
        var descriptor = new SpatialCollectionDescriptor("points", "Geographic2D", 2, "location", new SpatialIndexOptions(), SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));
        var ranges = new List<SpatialIndexRange>
        {
            new SpatialIndexRange(10, 20),
            new SpatialIndexRange(30, 40)
        };
        var plan = new SpatialQueryPlan(2, BoundingBox.From2D(-1, -1, 1, 1), ranges, "Haversine <= 1000m");

        var explain = SpatialDiagnostics.Explain(descriptor, plan);

        explain.CollectionName.Should().Be("points");
        explain.EngineName.Should().Be("Geographic2D");
        explain.Dimensions.Should().Be(2);
        explain.IndexRanges.Should().HaveCount(2);
        explain.CoveringBounds.Should().NotBeNull();
        explain.ToString().Should().Contain("Collection: points").And.Contain("Index ranges (2)");
    }

    [Fact]
    public void ExplainHandlesPlansWithoutCovering()
    {
        var descriptor = new SpatialCollectionDescriptor("points3d", "Cartesian3D", 3, "position", new SpatialIndexOptions(), SpatialEngineSettings.ForCartesian(BoundingBox.From3D(-10, -10, -10, 10, 10, 10)));
        var plan = new SpatialQueryPlan(3, null, new List<SpatialIndexRange>(), null);

        var explain = SpatialDiagnostics.Explain(descriptor, plan);

        explain.CoveringBounds.Should().BeNull();
        explain.IndexRanges.Should().BeEmpty();
        explain.ToString().Should().Contain("<none>");
    }
}
