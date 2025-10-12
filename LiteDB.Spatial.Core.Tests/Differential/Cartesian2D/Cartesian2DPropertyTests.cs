extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FsCheck;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Support;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class Cartesian2DPropertyTests
{
    [Fact]
    [Trait(TestCategories.Category, TestCategories.Oracle)]
    public void AxisAlignedBoundingBoxMatchesCoordinateInequalities()
    {
        FsCheckRunner.Check(nameof(AxisAlignedBoundingBoxMatchesCoordinateInequalities), Prop.ForAll<int>(seed =>
        {
            var random = new System.Random(NormalizeSeed(seed));
            var scenario = CartesianScenarioFactory.CreateBoundingBoxScenario(random);

            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var collection = database.GetCollection<PointDocument>("points");
            var descriptor = Spatial.UseCartesian2D(collection, x => x.Position, scenario.Domain);

            InsertSamples(collection, scenario.Samples);

            var engine = ResolveEngine(descriptor, scenario.Domain);
            var plan = SpatialCartesian2D.WithinBoundingBox(descriptor, scenario.Query);

            var encoded = SpatialPlanEvaluator.Encode(engine, scenario.Samples);
            var indexCandidates = SpatialPlanEvaluator.FilterByRanges(plan.IndexRanges, encoded);
            var covering = plan.CoveringBounds ?? scenario.Query;
            var prefilterCandidates = SpatialPlanEvaluator.FilterByBounds(covering, indexCandidates);

            var exact = SpatialPlanEvaluator.FilterByBounds(scenario.Query, prefilterCandidates)
                .Select(candidate => candidate.Index)
                .OrderBy(id => id)
                .ToArray();

            var expected = SpatialPlanEvaluator.FilterByBounds(scenario.Query, encoded)
                .Select(candidate => candidate.Index)
                .OrderBy(id => id)
                .ToArray();

            return exact.SequenceEqual(expected)
                .Label("Exact matches equal direct bounding box evaluation")
                .And(expected.All(id => indexCandidates.Any(c => c.Index == id)).Label("Index phase retains all hits"));
        }));
    }

    [Fact]
    [Trait(TestCategories.Category, TestCategories.Oracle)]
    public void ConvexPolygonMatchesNtsOracle()
    {
        FsCheckRunner.Check(nameof(ConvexPolygonMatchesNtsOracle), Prop.ForAll<int>(seed =>
        {
            var random = new System.Random(NormalizeSeed(seed));
            var scenario = CartesianScenarioFactory.CreatePolygonScenario(random);

            using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            var collection = database.GetCollection<PointDocument>("points");
            var descriptor = Spatial.UseCartesian2D(collection, x => x.Position, scenario.Domain);

            InsertSamples(collection, scenario.Samples);

            var engine = ResolveEngine(descriptor, scenario.Domain);
            var bounds = BoundingBox.From2D(
                scenario.Polygon.Outer.Min(p => p.Longitude),
                scenario.Polygon.Outer.Min(p => p.Latitude),
                scenario.Polygon.Outer.Max(p => p.Longitude),
                scenario.Polygon.Outer.Max(p => p.Latitude));

            var plan = SpatialCartesian2D.WithinBoundingBox(descriptor, bounds);
            var encoded = SpatialPlanEvaluator.Encode(engine, scenario.Samples);
            var indexCandidates = SpatialPlanEvaluator.FilterByRanges(plan.IndexRanges, encoded);
            var covering = plan.CoveringBounds ?? bounds;
            var prefilterCandidates = SpatialPlanEvaluator.FilterByBounds(covering, indexCandidates);

            var exact = prefilterCandidates
                .Where(candidate => NtsOracle.Contains(scenario.Polygon, candidate.Point))
                .Select(candidate => candidate.Index)
                .OrderBy(id => id)
                .ToArray();

            var expected = encoded
                .Where(candidate => NtsOracle.Contains(scenario.Polygon, candidate.Point))
                .Select(candidate => candidate.Index)
                .OrderBy(id => id)
                .ToArray();

            return exact.SequenceEqual(expected)
                .Label("Exact polygon matches align with NTS")
                .And(expected.All(id => indexCandidates.Any(c => c.Index == id)).Label("Index phase retains polygon hits"))
                .And(expected.All(id => prefilterCandidates.Any(c => c.Index == id)).Label("Bounding box prefilter retains polygon hits"));
        }));
    }

    private static int NormalizeSeed(int seed)
    {
        return seed == int.MinValue ? 0 : Math.Abs(seed);
    }

    private static void InsertSamples(BaseLiteDB.ILiteCollection<PointDocument> collection, IReadOnlyList<GeoPoint> points)
    {
        collection.DeleteAll();
        var documents = points.Select((point, index) => new PointDocument
        {
            Id = BaseLiteDB.ObjectId.NewObjectId(),
            Position = point
        }).ToList();
        collection.InsertBulk(documents);
    }

    private static Cartesian2DEngine ResolveEngine(SpatialCollectionDescriptor descriptor, BoundingBox domain)
    {
        if (descriptor.TryGetEngine(out var spatialEngine) && spatialEngine is Cartesian2DEngine cartesian)
        {
            return cartesian;
        }

        return new Cartesian2DEngine(descriptor.GeometryFieldName, domain, descriptor.Options);
    }

}
