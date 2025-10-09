#nullable enable

using System;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialResolverTests
{
    [Fact]
    public void Near2DProducesPlanFromGeographicEngine()
    {
        var options = new SpatialIndexOptions(precisionBits: 8, maxCoveringCells: 16, distanceTolerance: 5);
        var engine = new GeographicEngine("location", options);
        var descriptor = new SpatialCollectionDescriptor(
            collectionName: "places",
            engineName: GeographicEngine.EngineName,
            dimensions: 2,
            geometryFieldName: "location",
            options: options,
            settings: SpatialEngineSettings.ForGeographic(GeographicDistanceMode.Haversine),
            engine: engine);

        var resolver = new SpatialResolver(descriptor);
        var parameter = Expression.Parameter(typeof(GeoPoint), "p");
        var method = typeof(SpatialExpressions)
            .GetMethods()
            .Single(m => m.Name == nameof(SpatialExpressions.Near) && m.GetParameters()[0].ParameterType == typeof(GeoPoint));

        var call = Expression.Call(
            method,
            parameter,
            Expression.Constant(new GeoPoint(13.405, 52.52)),
            Expression.Constant(1_000d));

        var success = resolver.TryResolve(call, out var plan);

        success.Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(2);
        plan.IndexRanges.Should().NotBeEmpty();
        plan.ExactPredicateDescription.Should().Contain("Haversine");
    }

    [Fact]
    public void InBox3DUsesCartesianEngine()
    {
        var options = new SpatialIndexOptions(precisionBits: 10, maxCoveringCells: 32, distanceTolerance: 0.1);
        var domain = BoundingBox.From3D(-100, -100, -100, 100, 100, 100);
        var engine = new Cartesian3DEngine("position", domain, options);
        var descriptor = new SpatialCollectionDescriptor(
            collectionName: "points",
            engineName: Cartesian3DEngine.EngineName,
            dimensions: 3,
            geometryFieldName: "position",
            options: options,
            settings: SpatialEngineSettings.ForCartesian(domain),
            engine: engine);

        var resolver = new SpatialResolver(descriptor);
        var parameter = Expression.Parameter(typeof(GeoPoint3D), "p");
        var method = typeof(SpatialExpressions)
            .GetMethods()
            .Single(m => m.Name == nameof(SpatialExpressions.InBox) && m.GetParameters()[0].ParameterType == typeof(GeoPoint3D));

        var bounds = BoundingBox.From3D(-5, -5, -5, 5, 5, 5);
        var call = Expression.Call(method, parameter, Expression.Constant(bounds));

        resolver.TryResolve(call, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Dimensions.Should().Be(3);
        plan.CoveringBounds.Should().Be(bounds);
    }

    [Fact]
    public void ThrowsWhenEngineNotAttached()
    {
        var descriptor = new SpatialCollectionDescriptor(
            collectionName: "places",
            engineName: GeographicEngine.EngineName,
            dimensions: 2,
            geometryFieldName: "location",
            options: new SpatialIndexOptions());

        var resolver = new SpatialResolver(descriptor);
        var parameter = Expression.Parameter(typeof(GeoPoint), "p");
        var method = typeof(SpatialExpressions)
            .GetMethods()
            .Single(m => m.Name == nameof(SpatialExpressions.Near) && m.GetParameters()[0].ParameterType == typeof(GeoPoint));

        var call = Expression.Call(
            method,
            parameter,
            Expression.Constant(new GeoPoint(0, 0)),
            Expression.Constant(100d));

        Action action = () => resolver.TryResolve(call, out _);
        action.Should().Throw<SpatialMetadataException>().WithMessage("*not bound to a spatial engine*");
    }

    [Fact]
    public void RejectsDimensionMismatch()
    {
        var options = new SpatialIndexOptions();
        var engine = new Cartesian2DEngine("location", BoundingBox.From2D(-10, -10, 10, 10), options);
        var descriptor = new SpatialCollectionDescriptor(
            collectionName: "places",
            engineName: Cartesian2DEngine.EngineName,
            dimensions: 2,
            geometryFieldName: "location",
            options: options,
            settings: SpatialEngineSettings.ForCartesian(BoundingBox.From2D(-10, -10, 10, 10)),
            engine: engine);

        var resolver = new SpatialResolver(descriptor);
        var parameter = Expression.Parameter(typeof(GeoPoint3D), "p");
        var method = typeof(SpatialExpressions)
            .GetMethods()
            .Single(m => m.Name == nameof(SpatialExpressions.Near) && m.GetParameters()[0].ParameterType == typeof(GeoPoint3D));

        var call = Expression.Call(
            method,
            parameter,
            Expression.Constant(new GeoPoint3D(0, 0, 0)),
            Expression.Constant(10d));

        Action action = () => resolver.TryResolve(call, out _);
        action.Should().Throw<SpatialMetadataException>();
    }
}
