extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Linq;

public sealed class SpatialExpressionsQueryTests
{
    [Fact]
    public void GeographicNearQueryUsesIndexAndBoundingBox()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<City>("cities");
        var options = new SpatialIndexOptions(precisionBits: 16, maxCoveringCells: 32);
        var descriptor = Spatial.UseGeographic(collection, city => city.Location, options);

        InsertCities(collection);
        descriptor = Spatial.EnsurePointIndex(collection);

        var center = new GeoPoint(16.3738, 48.2082);
        var radius = 300_000d;

        var resolver = new SpatialResolver();
        var runtimeDescriptor = descriptor.WithEngine(new GeographicEngine(descriptor.GeometryFieldName, options));
        resolver.RegisterDescriptor(runtimeDescriptor, nameof(City.Location));

        Expression<Func<City, bool>> predicate = city => SpatialExpressions.Near(city.Location, center, radius);
        var methodCall = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();

        var explain = SpatialDiagnostics.Explain(plan!, runtimeDescriptor);
        ExplainPlanAssertions.AssertIndexedPlan(explain);
        explain.ExactPredicate.Should().NotBeNull();
        explain.ExactPredicate!.Should().ContainEquivalentOf("distance");

        var results = Spatial.Near(collection, city => city.Location, center, radius);

        results.Should().Contain(city => city.Name == "Vienna");
        results.Should().Contain(city => city.Name == "Prague");
        results.Should().NotContain(city => city.Name == "New York");
    }

    [Fact]
    public void CartesianBoundingBoxQueryReportsPrefiltering()
    {
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<PointOfInterest>("pois");
        var domain = BoundingBox.From2D(-100, -100, 100, 100);
        var options = new SpatialIndexOptions(precisionBits: 14, maxCoveringCells: 16);
        var descriptor = Spatial.UseCartesian2D(collection, poi => poi.Location, domain, options);

        InsertPoints(collection);
        descriptor = Spatial.EnsurePointIndex(collection);

        var bounds = BoundingBox.From2D(-10, -10, 10, 10);
        var resolver = new SpatialResolver();
        var runtimeDescriptor = descriptor.WithEngine(new Cartesian2DEngine(descriptor.GeometryFieldName, domain, options));
        resolver.RegisterDescriptor(runtimeDescriptor, nameof(PointOfInterest.Location));

        Expression<Func<PointOfInterest, bool>> predicate = poi => SpatialExpressions.InBox(poi.Location, bounds);
        var methodCall = (MethodCallExpression)predicate.Body;

        resolver.TryResolve(methodCall, out var plan).Should().BeTrue();
        plan.Should().NotBeNull();

        var explain = SpatialDiagnostics.Explain(plan!, runtimeDescriptor);
        ExplainPlanAssertions.AssertIndexedPlan(explain);
        explain.ExactPredicate.Should().NotBeNull();
        explain.ExactPredicate!.Should().ContainEquivalentOf("within");

        var results = Spatial.WithinBoundingBox(collection, poi => poi.Location, bounds);

        results.Should().HaveCount(2);
        results.Select(p => p.Name).Should().BeEquivalentTo(new[] { "Origin", "Neighbor" });
    }

    private static void InsertCities(BaseLiteDB.ILiteCollection<City> collection)
    {
        var cities = new List<City>
        {
            new("Vienna", new GeoPoint(16.3738, 48.2082)),
            new("Prague", new GeoPoint(14.4378, 50.0755)),
            new("New York", new GeoPoint(-73.935242, 40.73061))
        };

        collection.Insert(cities);
    }

    private static void InsertPoints(BaseLiteDB.ILiteCollection<PointOfInterest> collection)
    {
        var points = new List<PointOfInterest>
        {
            new("Origin", new GeoPoint(0, 0)),
            new("Neighbor", new GeoPoint(1, 1)),
            new("Far", new GeoPoint(40, 40))
        };

        collection.Insert(points);
    }

    private sealed record City(string Name, GeoPoint Location);

    private sealed record PointOfInterest(string Name, GeoPoint Location);
}
