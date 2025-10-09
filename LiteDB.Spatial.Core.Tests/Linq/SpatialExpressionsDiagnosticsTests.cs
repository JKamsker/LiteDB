using System;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests.Support;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialExpressionsDiagnosticsTests
{
    [Fact]
    public void NearQueriesExposeIndexOrdering()
    {
        var options = new SpatialIndexOptions(precisionBits: 10, maxCoveringCells: 128);
        var domain = BoundingBox.From2D(0, 0, 1, 1);
        var descriptor = new SpatialCollectionDescriptor("points", Cartesian2DEngine.EngineName, 2, nameof(SpatialDoc.Location), options, SpatialEngineSettings.ForCartesian(domain));
        var engine = new Cartesian2DEngine(nameof(SpatialDoc.Location), domain, options);
        var descriptorWithEngine = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithEngine, nameof(SpatialDoc.Location));

        Expression<Func<SpatialDoc, bool>> predicate = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(0.5, 0.5), 0.125);
        var call = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(call, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();

        var explain = SpatialDiagnostics.Explain(plan!, descriptorWithEngine);
        var breakdown = ExplainBreakdown.Create(explain);
        breakdown.ShouldDescribeIndexedPlan("Euclidean");
    }

    [Fact]
    public void BoundingBoxQueriesIncludePrefilters()
    {
        var options = new SpatialIndexOptions(precisionBits: 9, maxCoveringCells: 64);
        var domain = BoundingBox.From2D(-10, -10, 10, 10);
        var descriptor = new SpatialCollectionDescriptor("grid", Cartesian2DEngine.EngineName, 2, nameof(SpatialDoc.Location), options, SpatialEngineSettings.ForCartesian(domain));
        var engine = new Cartesian2DEngine(nameof(SpatialDoc.Location), domain, options);
        var descriptorWithEngine = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptorWithEngine, nameof(SpatialDoc.Location));

        var bounds = BoundingBox.From2D(-1, -1, 1, 1);
        Expression<Func<SpatialDoc, bool>> predicate = doc => SpatialExpressions.InBox(doc.Location, bounds);
        var call = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(call, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();

        var explain = SpatialDiagnostics.Explain(plan!, descriptorWithEngine);
        var breakdown = ExplainBreakdown.Create(explain);
        breakdown.ShouldDescribeIndexedPlan("Within");
    }

    private sealed class SpatialDoc
    {
        public GeoPoint Location { get; set; } = default!;
    }
}
