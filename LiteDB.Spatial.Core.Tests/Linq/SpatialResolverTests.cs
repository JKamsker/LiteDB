using System;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialResolverTests
{
    private sealed class GeoDocument
    {
        public GeoPoint Location { get; set; }
        public GeoPoint3D Position { get; set; }
    }

    [Fact]
    public void ResolveNear2D_UsesGeographicEngine()
    {
        var options = new SpatialIndexOptions(precisionBits: 8, maxCoveringCells: 4, distanceTolerance: 0.01);
        var engine = new GeographicEngine(nameof(GeoDocument.Location), options, GeographicDistanceMode.Haversine);
        var descriptor = new SpatialCollectionDescriptor(
            "places",
            GeographicEngine.EngineName,
            2,
            nameof(GeoDocument.Location),
            options,
            SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine),
            engine);

        var resolver = new SpatialResolver(descriptor);
        Expression<Func<GeoDocument, bool>> query = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(10, 20), 500);
        var expected = engine.PlanNear(new GeoPoint(10, 20), 500);

        resolver.TryResolve((MethodCallExpression)query.Body, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(expected.Dimensions);
        plan.IndexRanges.Should().BeEquivalentTo(expected.IndexRanges, options => options.WithStrictOrdering());
        plan.ExactPredicateDescription.Should().Be(expected.ExactPredicateDescription);
    }

    [Fact]
    public void ResolveNear3D_UsesCartesianEngine()
    {
        var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
        var options = new SpatialIndexOptions(precisionBits: 6, maxCoveringCells: 8, distanceTolerance: 0.1);
        var engine = new Cartesian3DEngine(nameof(GeoDocument.Position), domain, options);
        var descriptor = new SpatialCollectionDescriptor(
            "assets",
            Cartesian3DEngine.EngineName,
            3,
            nameof(GeoDocument.Position),
            options,
            SpatialEngineSettings.ForCartesian(domain),
            engine);

        var resolver = new SpatialResolver(descriptor);
        var center = new GeoPoint3D(1, 2, 3);
        Expression<Func<GeoDocument, bool>> query = doc => SpatialExpressions.Near(doc.Position, center, 15.5);
        var expected = engine.PlanNear(center, 15.5);

        resolver.TryResolve((MethodCallExpression)query.Body, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(expected.Dimensions);
        plan.IndexRanges.Should().BeEquivalentTo(expected.IndexRanges, options => options.WithStrictOrdering());
        plan.ExactPredicateDescription.Should().Be(expected.ExactPredicateDescription);
    }

    [Fact]
    public void ResolveInBox_UsesBoundingBoxPlan()
    {
        var options = new SpatialIndexOptions();
        var domain = BoundingBox.From2D(-1, -1, 1, 1);
        var engine = new Cartesian2DEngine(nameof(GeoDocument.Location), domain, options);
        var descriptor = new SpatialCollectionDescriptor(
            "grid",
            Cartesian2DEngine.EngineName,
            2,
            nameof(GeoDocument.Location),
            options,
            SpatialEngineSettings.ForCartesian(domain),
            engine);

        var resolver = new SpatialResolver(descriptor);
        var bounds = BoundingBox.From2D(-0.5, -0.5, 0.5, 0.5);
        Expression<Func<GeoDocument, bool>> query = doc => SpatialExpressions.InBox(doc.Location, bounds);
        var expected = engine.PlanWithin(bounds);

        resolver.TryResolve((MethodCallExpression)query.Body, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(expected.Dimensions);
        plan.IndexRanges.Should().BeEquivalentTo(expected.IndexRanges, options => options.WithStrictOrdering());
        plan.CoveringBounds.Should().Be(expected.CoveringBounds);
        plan.ExactPredicateDescription.Should().Be(expected.ExactPredicateDescription);
    }

    [Fact]
    public void ResolveNearWithoutEngineThrows()
    {
        var options = new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor(
            "places",
            GeographicEngine.EngineName,
            2,
            nameof(GeoDocument.Location),
            options,
            SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));

        var resolver = new SpatialResolver(descriptor);
        Expression<Func<GeoDocument, bool>> query = doc => SpatialExpressions.Near(doc.Location, new GeoPoint(0, 0), 10);

        Action action = () => resolver.TryResolve((MethodCallExpression)query.Body, out _);
        action.Should().Throw<SpatialMetadataException>()
            .WithMessage("*does not have a runtime spatial engine attached*");
    }
}

