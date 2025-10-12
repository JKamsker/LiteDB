extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Differential;
using LiteDB.Spatial.Core.Tests.TestSupport;
using NetTopologySuite.Geometries;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class Cartesian2DPropertyTests
{

    [SeededProperty(MaxTest = 150)]
    [Trait("Category", "Oracle")]
    public Property WithinBoundingBoxMatchesOracle()
    {
        var generator = Cartesian2DGenerators.BoundingBoxes();
        var arbitrary = Arb.From(generator);
        return Prop.ForAll<CartesianBoundingBoxScenario>(arbitrary, scenario =>
        {
            try
            {
                using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
                var collection = database.GetCollection<CartesianDocument>("points");

                Spatial.UseCartesian2D(collection, x => x.Position, scenario.Domain);

                collection.DeleteAll();
                var documents = scenario.Points.Select(p => new CartesianDocument(p.Id, p.Position)).ToList();
                collection.Insert(documents);

                Spatial.EnsurePointIndex(collection);

                var expectedPolygon = NtsOracle.CreateAxisAlignedBox(scenario.Query);

                var results = Spatial.WithinBoundingBox(collection, x => x.Position, scenario.Query);
                var actualIds = results.Select(r => r.Id).OrderBy(id => id).ToArray();
                var expectedIds = scenario.Points
                    .Where(p => NtsOracle.Covers(expectedPolygon, p.Position))
                    .Select(p => p.Id)
                    .OrderBy(id => id)
                    .ToArray();

                AssertWithTolerance(scenario.Query, scenario.Points, actualIds, expectedIds);
                return true;
            }
            catch (Exception ex)
            {
                var payload = new
                {
                    Domain = scenario.Domain.GetValues().ToArray(),
                    Query = scenario.Query.GetValues().ToArray(),
                    Points = scenario.Points.Select(p => new { p.Id, X = p.Position.Longitude, Y = p.Position.Latitude })
                };
                CounterexampleRecorder.Record("cartesian2d-aabb", payload, ex);
                throw;
            }
        });
    }

    [SeededProperty(MaxTest = 120)]
    [Trait("Category", "Oracle")]
    public Property ConvexPolygonBoundingBoxMatchesOracle()
    {
        var generator = Cartesian2DGenerators.ConvexPolygons();
        var arbitrary = Arb.From(generator);
        return Prop.ForAll<CartesianPolygonScenario>(arbitrary, scenario =>
        {
            try
            {
                using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
                var collection = database.GetCollection<CartesianDocument>("points");

                Spatial.UseCartesian2D(collection, x => x.Position, scenario.Domain);

                collection.DeleteAll();
                var documents = scenario.Points.Select(p => new CartesianDocument(p.Id, p.Position)).ToList();
                collection.Insert(documents);
                Spatial.EnsurePointIndex(collection);

                var results = Spatial.WithinBoundingBox(collection, x => x.Position, scenario.Query);
                var actualIds = results.Select(r => r.Id).OrderBy(id => id).ToArray();

                var expectedIds = scenario.Points
                    .Where(p => NtsOracle.Covers(scenario.Polygon, p.Position))
                    .Select(p => p.Id)
                    .OrderBy(id => id)
                    .ToArray();

                var expectedBoundingBoxPolygon = NtsOracle.CreateAxisAlignedBox(scenario.Query);
                var boundingBoxIds = scenario.Points
                    .Where(p => NtsOracle.Covers(expectedBoundingBoxPolygon, p.Position))
                    .Select(p => p.Id)
                    .OrderBy(id => id)
                    .ToArray();

                AssertWithTolerance(scenario.Query, scenario.Points, actualIds, boundingBoxIds);
                return true;
            }
            catch (Exception ex)
            {
                var payload = new
                {
                    Domain = scenario.Domain.GetValues().ToArray(),
                    Query = scenario.Query.GetValues().ToArray(),
                    Hull = scenario.HullPoints
                        .Select(p => new { p.Longitude, p.Latitude })
                        .ToList()
                };
                CounterexampleRecorder.Record("cartesian2d-convex", payload, ex);
                throw;
            }
        });
    }

    private static void AssertWithTolerance(
        BoundingBox bounds,
        IReadOnlyList<CartesianPoint> points,
        IReadOnlyCollection<int> actual,
        IReadOnlyCollection<int> expected)
    {
        var values = bounds.GetValues();
        var spanX = values[2] - values[0];
        var spanY = values[3] - values[1];
        var tolerance = SpatialTestTolerances.Cartesian(Math.Max(spanX, spanY));

        var expectedSet = expected.ToHashSet();
        var actualSet = actual.ToHashSet();

        var missing = expectedSet.Except(actualSet).ToArray();
        var unexpected = actualSet.Except(expectedSet).ToArray();

        var nearBoundaryMissing = missing
            .Where(id => IsNearBoundary(points.First(p => p.Id == id).Position, bounds, tolerance))
            .ToArray();
        var nearBoundaryUnexpected = unexpected
            .Where(id => IsNearBoundary(points.First(p => p.Id == id).Position, bounds, tolerance))
            .ToArray();

        missing.Except(nearBoundaryMissing).Should().BeEmpty();
        unexpected.Except(nearBoundaryUnexpected).Should().BeEmpty();

        var normalizedExpected = expectedSet.Except(nearBoundaryMissing).OrderBy(id => id).ToArray();
        var normalizedActual = actualSet.Except(nearBoundaryUnexpected).OrderBy(id => id).ToArray();

        normalizedActual.Should().Equal(normalizedExpected);
    }

    private static bool IsNearBoundary(GeoPoint point, BoundingBox bounds, double tolerance)
    {
        var values = bounds.GetValues();
        var minX = values[0];
        var minY = values[1];
        var maxX = values[2];
        var maxY = values[3];

        if (point.Longitude < minX - tolerance || point.Longitude > maxX + tolerance)
        {
            return false;
        }

        if (point.Latitude < minY - tolerance || point.Latitude > maxY + tolerance)
        {
            return false;
        }

        var horizontalMargin = Math.Min(point.Longitude - minX, maxX - point.Longitude);
        var verticalMargin = Math.Min(point.Latitude - minY, maxY - point.Latitude);
        var margin = Math.Min(horizontalMargin, verticalMargin);

        return margin <= tolerance;
    }

    private sealed record CartesianDocument(int Id, GeoPoint Position);
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SeededPropertyAttribute : PropertyAttribute
{
    public SeededPropertyAttribute()
    {
        var seedValue = Environment.GetEnvironmentVariable("FS_CHECK_SEED");
        if (!string.IsNullOrWhiteSpace(seedValue))
        {
            Replay = seedValue.Trim();
        }
    }
}
