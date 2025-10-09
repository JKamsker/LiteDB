#nullable enable

using System;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialResolverTests
{
    [Fact]
    public void TryResolveNear2DReturnsPlanFromEngine()
    {
        var options = new SpatialIndexOptions(precisionBits: 10, maxCoveringCells: 32, distanceTolerance: 5);
        var descriptor = new SpatialCollectionDescriptor("points", GeographicEngine.EngineName, 2, "location", options, SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));
        var engine = new GeographicEngine("location", options, GeographicDistanceMode.Haversine);
        descriptor = descriptor.WithEngine(engine);

        var resolver = new SpatialResolver(descriptor);
        var method = typeof(SpatialExpressions).GetMethod(nameof(SpatialExpressions.Near), new[] { typeof(GeoPoint), typeof(GeoPoint), typeof(double) })!;
        var candidate = Expression.Parameter(typeof(GeoPoint), "candidate");
        var center = Expression.Constant(new GeoPoint(13.405, 52.52));
        var radius = Expression.Constant(500d);
        var call = Expression.Call(method, candidate, center, radius);

        var result = resolver.TryResolve(call, out var plan);

        result.Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.CoveringBounds.Should().NotBeNull();
        plan.ExactPredicateDescription.Should().Contain("Haversine");
    }

    [Fact]
    public void TryResolveNear3DUsesInjectedEngine()
    {
        var options = new SpatialIndexOptions(precisionBits: 8, maxCoveringCells: 16, distanceTolerance: 0.01);
        var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
        var descriptor = new SpatialCollectionDescriptor("points3d", Cartesian3DEngine.EngineName, 3, "position", options, SpatialEngineSettings.ForCartesian(domain));
        var resolver = new SpatialResolver(descriptor, _ => new Cartesian3DEngine("position", domain, options));

        var method = typeof(SpatialExpressions).GetMethod(nameof(SpatialExpressions.Near), new[] { typeof(GeoPoint3D), typeof(GeoPoint3D), typeof(double) })!;
        var candidate = Expression.Parameter(typeof(GeoPoint3D), "candidate");
        var center = Expression.Constant(new GeoPoint3D(1, 2, 3));
        var radius = Expression.Constant(25d);
        var call = Expression.Call(method, candidate, center, radius);

        resolver.TryResolve(call, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(3);
        plan.CoveringBounds.Should().NotBeNull();
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("Euclidean3D");
    }

    [Fact]
    public void TryResolveInBoxReturnsPlan()
    {
        var options = new SpatialIndexOptions(precisionBits: 9, maxCoveringCells: 16, distanceTolerance: 2);
        var descriptor = new SpatialCollectionDescriptor("points", GeographicEngine.EngineName, 2, "location", options, SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));
        var engine = new GeographicEngine("location", options, GeographicDistanceMode.Haversine);
        descriptor = descriptor.WithEngine(engine);

        var resolver = new SpatialResolver(descriptor);
        var method = typeof(SpatialExpressions).GetMethod(nameof(SpatialExpressions.InBox), new[] { typeof(GeoPoint), typeof(BoundingBox) })!;
        var candidate = Expression.Parameter(typeof(GeoPoint), "candidate");
        var bounds = Expression.Constant(BoundingBox.From2D(-10, -10, 10, 10));
        var call = Expression.Call(method, candidate, bounds);

        resolver.TryResolve(call, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.CoveringBounds.Should().NotBeNull();
        plan.IndexRanges.Should().NotBeEmpty();
    }

    [Fact]
    public void MissingEngineThrowsMetadataException()
    {
        var descriptor = new SpatialCollectionDescriptor("points", GeographicEngine.EngineName, 2, "location", new SpatialIndexOptions(), SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));
        var resolver = new SpatialResolver(descriptor);
        var method = typeof(SpatialExpressions).GetMethod(nameof(SpatialExpressions.Near), new[] { typeof(GeoPoint), typeof(GeoPoint), typeof(double) })!;
        var candidate = Expression.Parameter(typeof(GeoPoint), "candidate");
        var call = Expression.Call(method, candidate, Expression.Constant(new GeoPoint(0, 0)), Expression.Constant(10d));

        Action action = () => resolver.TryResolve(call, out _);
        action.Should().Throw<SpatialMetadataException>().WithMessage("*runtime spatial engine*");
    }

    [Fact]
    public void ParameterBasedArgumentsAreNotSupported()
    {
        var options = new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor("points", GeographicEngine.EngineName, 2, "location", options, SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine));
        var engine = new GeographicEngine("location", options, GeographicDistanceMode.Haversine);
        descriptor = descriptor.WithEngine(engine);
        var resolver = new SpatialResolver(descriptor);

        var method = typeof(SpatialExpressions).GetMethod(nameof(SpatialExpressions.Near), new[] { typeof(GeoPoint), typeof(GeoPoint), typeof(double) })!;
        var candidate = Expression.Parameter(typeof(GeoPoint), "candidate");
        var radiusParameter = Expression.Parameter(typeof(double), "radius");
        var call = Expression.Call(method, candidate, Expression.Constant(new GeoPoint(0, 0)), radiusParameter);

        Action action = () => resolver.TryResolve(call, out _);
        action.Should().Throw<NotSupportedException>();
    }
}
