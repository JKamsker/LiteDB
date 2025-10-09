extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

public sealed class Cartesian3DAabbTests
{
    private readonly ITestOutputHelper _output;

    public Cartesian3DAabbTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BoundingBoxesMatchManualInequalities()
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

        var boundingField = descriptor.Options.BoundingBoxFieldName;
        var mbbById = new Dictionary<int, double[]>(fixture.Points.Count);
        var pointById = fixture.Points.ToDictionary(p => p.Id);

        foreach (var document in collection.FindAll())
        {
            var id = document["_id"].AsInt32;
            var mbb = document[boundingField].AsArray.Select(v => v.AsDouble).ToArray();
            mbb.Length.Should().Be(6, "3D bounding boxes must contain six values");
            for (var axis = 0; axis < 3; axis++)
            {
                var min = mbb[axis];
                var max = mbb[axis + 3];
                max.Should().BeGreaterOrEqualTo(min, "bounding boxes must be normalized");
            }

            var point = pointById[id];
            mbb[0].Should().BeApproximately(point.X, 1e-6);
            mbb[3].Should().BeApproximately(point.X, 1e-6);
            mbb[1].Should().BeApproximately(point.Y, 1e-6);
            mbb[4].Should().BeApproximately(point.Y, 1e-6);
            mbb[2].Should().BeApproximately(point.Z, 1e-6);
            mbb[5].Should().BeApproximately(point.Z, 1e-6);

            mbbById[id] = mbb;
        }

        var fallbackTriggered = false;

        foreach (var query in fixture.BoxQueries)
        {
            var bounds = query.ToBoundingBox();
            var plan = SpatialCartesian3D.WithinBoundingBox(descriptor, bounds);
            _output.WriteLine(
                $"[box:{query.Name}] requested={plan.CoveringDiagnostics.RequestedRangeCount} returned={plan.CoveringDiagnostics.ReturnedRangeCount} effective={plan.CoveringDiagnostics.EffectiveRangeCount} estimated={plan.CoveringDiagnostics.EstimatedCellCount} maxFallback={plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback} enumerationFallback={plan.CoveringDiagnostics.UsedEnumerationFallback}");

            var triggeredFallback = plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback || plan.CoveringDiagnostics.UsedEnumerationFallback;
            fallbackTriggered |= triggeredFallback;

            var manualMatches = mbbById
                .Where(pair => Contains(pair.Value, query))
                .Select(pair => pair.Key)
                .OrderBy(id => id)
                .ToList();

            var coordinateMatches = fixture.Points
                .Where(point => Contains(point, query))
                .Select(point => point.Id)
                .OrderBy(id => id)
                .ToList();

            try
            {
                manualMatches.Should().Equal(coordinateMatches, $"manual inequality filter should match coordinate evaluation for {query.Name}");
            }
            catch (Xunit.Sdk.XunitException)
            {
                FailureReporter.Record($"box-{query.Name}", new
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
                    BoundingBoxes = mbbById,
                    CoordinateMatches = coordinateMatches,
                    ManualMatches = manualMatches
                });

                throw;
            }
        }

        fallbackTriggered.Should().BeTrue("At least one bounding box query should trigger a covering fallback");
    }

    private static bool Contains(double[] mbb, BoxQuery query)
    {
        return mbb[0] >= query.Min.X && mbb[3] <= query.Max.X
            && mbb[1] >= query.Min.Y && mbb[4] <= query.Max.Y
            && mbb[2] >= query.Min.Z && mbb[5] <= query.Max.Z;
    }

    private static bool Contains(LatticePoint point, BoxQuery query)
    {
        return point.X >= query.Min.X && point.X <= query.Max.X
            && point.Y >= query.Min.Y && point.Y <= query.Max.Y
            && point.Z >= query.Min.Z && point.Z <= query.Max.Z;
    }
}
