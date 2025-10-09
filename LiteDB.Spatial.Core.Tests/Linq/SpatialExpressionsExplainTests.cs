#nullable enable

extern alias LiteDbBase;

using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialExpressionsExplainTests
{
    [Fact]
    public void Cartesian2DNearExplainUsesIndexAndBoundingPrefilters()
    {
        var fixtures = MortonLocalityFixtures.Load();
        var cartesian2D = fixtures.Cases.First(c => c.Dimensions == 2);
        var domain = BoundingBox.From2D(0, 0, 1, 1);
        var options = new SpatialIndexOptions(precisionBits: cartesian2D.PrecisionBits, maxCoveringCells: 64);
        var points = cartesian2D.GeneratePoints();
        var expectedCodes = cartesian2D.GetExpectedCodes();

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<CartesianPoint>("cartesian2d");

        var descriptor = Spatial.UseCartesian2D(collection, x => x.Position, domain, options);
        collection.Insert(points.Select(p => new CartesianPoint(new GeoPoint(p[0], p[1]))));
        Spatial.EnsurePointIndex(collection);

        var center = new GeoPoint(cartesian2D.QueryCenter[0], cartesian2D.QueryCenter[1]);
        var radius = cartesian2D.QueryRadius;

        var results = Spatial.Near(collection, x => x.Position, center, radius);

        results.Should().NotBeEmpty();

        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptor, nameof(CartesianPoint.Position));
        Expression<Func<CartesianPoint, bool>> predicate = point => SpatialExpressions.Near(point.Position, center, radius);
        var planResolved = resolver.TryResolve((MethodCallExpression)predicate.Body, out var plan);
        planResolved.Should().BeTrue();
        plan.Should().NotBeNull();

        var explain = SpatialDiagnostics.Explain(plan!, descriptor);
        SpatialExplainAssertions.AssertIndexedExecution(explain);

        var reduction = LocalityMetrics.MeasureCandidateReduction(explain.IndexRanges, expectedCodes);
        reduction.IndexCandidates.Should().BeLessThan(reduction.TotalCandidates);
    }

    [Fact]
    public void Cartesian3DNearExplainPreservesIndexOrdering()
    {
        var fixtures = MortonLocalityFixtures.Load();
        var cartesian3D = fixtures.Cases.First(c => c.Dimensions == 3);
        var domain = BoundingBox.From3D(0, 0, 0, 1, 1, 1);
        var options = new SpatialIndexOptions(precisionBits: cartesian3D.PrecisionBits, maxCoveringCells: 128);
        var points = cartesian3D.GeneratePoints();
        var expectedCodes = cartesian3D.GetExpectedCodes();

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<CartesianPoint3D>("cartesian3d");

        var descriptor = Spatial.UseCartesian3D(collection, x => x.Position, domain, options);
        collection.Insert(points.Select(p => new CartesianPoint3D(new GeoPoint3D(p[0], p[1], p[2]))));
        Spatial.EnsurePointIndex(collection);

        var center = new GeoPoint3D(cartesian3D.QueryCenter[0], cartesian3D.QueryCenter[1], cartesian3D.QueryCenter[2]);
        var radius = cartesian3D.QueryRadius;

        var results = Spatial.Near(collection, x => x.Position, center, radius);

        results.Should().NotBeEmpty();

        var resolver = new SpatialResolver();
        resolver.RegisterDescriptor(descriptor, nameof(CartesianPoint3D.Position));
        Expression<Func<CartesianPoint3D, bool>> predicate = point => SpatialExpressions.Near(point.Position, center, radius);
        var planResolved = resolver.TryResolve((MethodCallExpression)predicate.Body, out var plan);
        planResolved.Should().BeTrue();
        plan.Should().NotBeNull();

        var explain = SpatialDiagnostics.Explain(plan!, descriptor);
        SpatialExplainAssertions.AssertIndexedExecution(explain);

        var reduction = LocalityMetrics.MeasureCandidateReduction(explain.IndexRanges, expectedCodes);
        reduction.IndexCandidates.Should().BeLessThan(reduction.TotalCandidates);
    }

    private sealed record CartesianPoint(GeoPoint Position);

    private sealed record CartesianPoint3D(GeoPoint3D Position);
}
