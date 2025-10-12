extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

[Category("differential")]
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
            fixture.Domain,
            new SpatialIndexOptions(
                fixture.Options.PrecisionBits,
                fixture.Options.MaxCoveringCells,
                fixture.Options.DistanceTolerance,
                fixture.Options.IndexFieldName,
                fixture.Options.BoundingBoxFieldName));

        var boundingField = descriptor.Options.BoundingBoxFieldName;
        var mbbById = new Dictionary<string, double[]>(fixture.Points.Count, StringComparer.Ordinal);
        var pointById = fixture.Points.ToDictionary(p => p.Id, StringComparer.Ordinal);

        foreach (var document in collection.FindAll())
        {
            var idValue = document["_id"];
            var id = idValue.Type switch
            {
                BaseLiteDB.BsonType.Int32 => idValue.AsInt32.ToString(CultureInfo.InvariantCulture),
                BaseLiteDB.BsonType.Int64 => idValue.AsInt64.ToString(CultureInfo.InvariantCulture),
                BaseLiteDB.BsonType.Double => idValue.AsDouble.ToString(CultureInfo.InvariantCulture),
                BaseLiteDB.BsonType.String => idValue.AsString,
                _ => idValue.ToString()
            };
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

        foreach (var query in fixture.AabbQueries)
        {
            var bounds = query.Bounds;
            var plan = SpatialCartesian3D.WithinBoundingBox(descriptor, bounds);
            _output.WriteLine(
                $"[box:{query.Id}] requested={plan.CoveringDiagnostics.RequestedRangeCount} returned={plan.CoveringDiagnostics.ReturnedRangeCount} effective={plan.CoveringDiagnostics.EffectiveRangeCount} estimated={plan.CoveringDiagnostics.EstimatedCellCount} maxFallback={plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback} enumerationFallback={plan.CoveringDiagnostics.UsedEnumerationFallback}");

            var triggeredFallback = plan.CoveringDiagnostics.UsedMaxCoveringCellsFallback || plan.CoveringDiagnostics.UsedEnumerationFallback;
            fallbackTriggered |= triggeredFallback;

            var manualMatches = mbbById
                .Where(pair => Contains(pair.Value, query))
                .Select(pair => pair.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            var coordinateMatches = fixture.Points
                .Where(point => Contains(point, query))
                .Select(point => point.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            try
            {
                manualMatches.Should().Equal(coordinateMatches, $"manual inequality filter should match coordinate evaluation for {query.Id}");
            }
            catch (Xunit.Sdk.XunitException)
            {
                FailureReporter.Record($"box-{query.Id}", new
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
                        plan.CoveringDiagnostics.UsedEnumerationFallback,
                        descriptor.Options.MaxCoveringCells
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

    private static bool Contains(double[] mbb, Cartesian3DAabbQuery query)
    {
        var bounds = query.Bounds.GetValues();
        return mbb[0] >= bounds[0] && mbb[3] <= bounds[3]
            && mbb[1] >= bounds[1] && mbb[4] <= bounds[4]
            && mbb[2] >= bounds[2] && mbb[5] <= bounds[5];
    }

    private static bool Contains(Cartesian3DLatticePoint point, Cartesian3DAabbQuery query)
    {
        var bounds = query.Bounds.GetValues();
        return point.X >= bounds[0] && point.X <= bounds[3]
            && point.Y >= bounds[1] && point.Y <= bounds[4]
            && point.Z >= bounds[2] && point.Z <= bounds[5];
    }
}
