extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial.Core.Tests.TestSupport;
using LiteDB.Spatial;
using Xunit;
using Xunit.Abstractions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

public sealed class Cartesian3DDifferentialTests
{
    private readonly ITestOutputHelper _output;

    public Cartesian3DDifferentialTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NearQueriesMatchMathNetOracle()
    {
        var fixture = Cartesian3DLatticeFixtureLoader.Load();
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("points");

        foreach (var point in fixture.Points)
        {
            collection.Insert(new BaseLiteDB.BsonDocument
            {
                ["_id"] = point.Id,
                ["position"] = new BaseLiteDB.BsonDocument
                {
                    ["x"] = point.X,
                    ["y"] = point.Y,
                    ["z"] = point.Z
                }
            });
        }

        var metadata = new SpatialMetadataStore(database);
        var descriptor = SpatialCartesian3D.EnsurePointIndex(
            metadata,
            collection,
            "position",
            fixture.DomainBoundingBox,
            new SpatialIndexOptions(
                precisionBits: 9,
                maxCoveringCells: fixture.MaxCoveringCells,
                distanceTolerance: fixture.DistanceTolerance));

        var engine = descriptor.Engine.Should().BeOfType<Cartesian3DEngine>().Subject;
        var tolerance = descriptor.Options.DistanceTolerance;
        var fallbackTriggered = false;

        foreach (var query in fixture.NearQueries)
        {
            var center = query.Center.ToGeoPoint3D();
            var plan = SpatialCartesian3D.Near(descriptor, center, query.Radius);
            _output.WriteLine(
                $"[near:{query.Name}] requested={plan.CoveringDiagnostics.RequestedRangeCount} returned={plan.CoveringDiagnostics.ReturnedRangeCount} effective={plan.CoveringDiagnostics.EffectiveRangeCount} estimated={plan.CoveringDiagnostics.EstimatedCellCount} maxFallback={plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback} enumerationFallback={plan.CoveringDiagnostics.UsedEnumerationFallback}");

            var triggeredFallback = plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback || plan.CoveringDiagnostics.UsedEnumerationFallback;
            fallbackTriggered |= triggeredFallback;

            var comparisons = CompareDistances(fixture, center, engine, query.Radius, tolerance);

            try
            {
                var liteMatches = comparisons
                    .Where(c => c.LiteMatch)
                    .Select(c => c.PointId)
                    .OrderBy(id => id)
                    .ToList();

                var mathMatches = comparisons
                    .Where(c => c.MathNetMatch)
                    .Select(c => c.PointId)
                    .OrderBy(id => id)
                    .ToList();

                liteMatches.Should().Equal(mathMatches, $"LiteDB and MathNet should agree on membership for {query.Name}");

                foreach (var comparison in comparisons)
                {
                    Math.Abs(comparison.LiteDistance - comparison.MathNetDistance)
                        .Should()
                        .BeLessOrEqualTo(query.DistanceTolerance, $"distance tolerance should hold for point {comparison.PointId} in {query.Name}");
                }
            }
            catch (Xunit.Sdk.XunitException)
            {
                FailureReporter.Record($"near-{query.Name}", new
                {
                    Query = query,
                    descriptor.Options.DistanceTolerance,
                    Plan = new
                    {
                        plan.CoveringDiagnostics.RequestedRangeCount,
                        plan.CoveringDiagnostics.ReturnedRangeCount,
                        plan.CoveringDiagnostics.EffectiveRangeCount,
                        plan.CoveringDiagnostics.EstimatedCellCount,
                        plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback,
                        plan.CoveringDiagnostics.UsedEnumerationFallback
                    },
                    Points = fixture.Points,
                    Center = new { center.X, center.Y, center.Z },
                    query.Radius,
                    Comparisons = comparisons
                });

                throw;
            }
        }

        fallbackTriggered.Should().BeTrue("At least one query should trigger a covering fallback");
    }

    private static List<DistanceComparison> CompareDistances(
        Cartesian3DLatticeFixture fixture,
        GeoPoint3D center,
        Cartesian3DEngine engine,
        double radius,
        double tolerance)
    {
        var comparisons = new List<DistanceComparison>(fixture.Points.Count);

        foreach (var point in fixture.Points)
        {
            var candidate = point.ToGeoPoint();
            var liteDistance = engine.Distance.Distance(candidate, center);
            var mathDistance = MathNetOracle3D.Distance(candidate, center);
            var liteMatch = liteDistance <= radius + tolerance;
            var mathMatch = mathDistance <= radius + tolerance;

            comparisons.Add(new DistanceComparison(point.Id, liteDistance, mathDistance, liteMatch, mathMatch));
        }

        return comparisons;
    }

    private sealed record DistanceComparison(int PointId, double LiteDistance, double MathNetDistance, bool LiteMatch, bool MathNetMatch);
}
