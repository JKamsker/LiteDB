extern alias LiteDbBase;

#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Support;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class Cartesian2DNearFixtureTests
{
    [Fact]
    public void NearQueriesMatchEuclideanExpectations()
    {
        var path = Path.Combine("tests", "fixtures", "point_clouds", "cartesian_uniform.json");
        var fixture = PointCloudFixture.Load(path);

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<PointDocument>("points");
        var descriptor = Spatial.UseCartesian2D(collection, x => x.Position, fixture.Domain);

        var documents = fixture.Points.Select((point, index) => new PointDocument
        {
            Id = BaseLiteDB.ObjectId.NewObjectId(),
            Position = new GeoPoint(point.X, point.Y)
        }).ToList();
        if (documents.GroupBy(d => d.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Fixture produced duplicate identifiers.");
        }
        collection.InsertBulk(documents);

        var points = documents.Select(d => d.Position).ToList();
        var idToIndex = documents.Select((doc, idx) => (doc.Id, idx)).ToDictionary(pair => pair.Id, pair => pair.idx);
        var engine = descriptor.TryGetEngine(out var runtimeEngine) && runtimeEngine is Cartesian2DEngine existing
            ? existing
            : new Cartesian2DEngine(descriptor.GeometryFieldName, fixture.Domain, descriptor.Options);

        var bsonCollection = database.GetCollection<BaseLiteDB.BsonDocument>("points");
        LiteDB.Spatial.SpatialBackfill.Run(bsonCollection, descriptor, engine);

        var encoded = SpatialPlanEvaluator.Encode(engine, points);

        foreach (var query in fixture.Queries)
        {
            var center = new GeoPoint(query.CenterX, query.CenterY);
            var plan = SpatialCartesian2D.Near(descriptor, center, query.Radius);
            var explain = SpatialDiagnostics.Explain(plan, descriptor);
            explain.EngineName.Should().Be(Cartesian2DEngine.EngineName);
            explain.RangeCount.Should().Be(plan.IndexRanges.Count);

            var indexCandidates = SpatialPlanEvaluator.FilterByRanges(plan.IndexRanges, encoded);
            var covering = plan.CoveringBounds ?? BoundingBox.From2D(center.Longitude - query.Radius, center.Latitude - query.Radius, center.Longitude + query.Radius, center.Latitude + query.Radius);
            var prefilterCandidates = SpatialPlanEvaluator.FilterByBounds(covering, indexCandidates);

            var tolerance = NumericTolerance.Euclidean(query.Radius);
            var exactCandidates = prefilterCandidates
                .Where(candidate =>
                {
                    var dx = candidate.Point.Longitude - center.Longitude;
                    var dy = candidate.Point.Latitude - center.Latitude;
                    var expected = Math.Sqrt((dx * dx) + (dy * dy));
                    var actual = engine.Distance.Distance(candidate.Point, center);
                    actual.Should().BeApproximately(expected, tolerance);
                    return actual <= query.Radius + tolerance;
                })
                .ToList();

            var exactIds = exactCandidates.Select(candidate => candidate.Index).OrderBy(id => id).ToArray();

            var results = Spatial.Near(collection, x => x.Position, center, query.Radius);
            var resultIndexes = results.Select(r => idToIndex[r.Id]).OrderBy(index => index).ToArray();
            resultIndexes.Should().Equal(exactIds);

            indexCandidates.Count.Should().BeGreaterOrEqualTo(prefilterCandidates.Count);
            prefilterCandidates.Count.Should().BeGreaterOrEqualTo(exactCandidates.Count);
            exactCandidates.Count.Should().Be(results.Count);
        }
    }

    [Fact]
    public void MortonOrderingIsStableForRepeatedPlans()
    {
        var path = Path.Combine("tests", "fixtures", "point_clouds", "cartesian_uniform.json");
        var fixture = PointCloudFixture.Load(path);
        var query = fixture.Queries.First();

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<PointDocument>("points");
        var descriptor = Spatial.UseCartesian2D(collection, x => x.Position, fixture.Domain);

        var documents = fixture.Points.Select((point, index) => new PointDocument
        {
            Id = BaseLiteDB.ObjectId.NewObjectId(),
            Position = new GeoPoint(point.X, point.Y)
        }).ToList();
        if (documents.GroupBy(d => d.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Fixture produced duplicate identifiers.");
        }
        collection.InsertBulk(documents);

        var points = documents.Select(d => d.Position).ToList();
        var idToIndex = documents.Select((doc, idx) => (doc.Id, idx)).ToDictionary(pair => pair.Id, pair => pair.idx);
        var engine = descriptor.TryGetEngine(out var runtimeEngine) && runtimeEngine is Cartesian2DEngine existing
            ? existing
            : new Cartesian2DEngine(descriptor.GeometryFieldName, fixture.Domain, descriptor.Options);

        var bsonCollection = database.GetCollection<BaseLiteDB.BsonDocument>("points");
        LiteDB.Spatial.SpatialBackfill.Run(bsonCollection, descriptor, engine);

        var encoded = SpatialPlanEvaluator.Encode(engine, points);
        var baseline = Array.Empty<int>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var plan = SpatialCartesian2D.Near(descriptor, query.Center, query.Radius);
            var indexCandidates = SpatialPlanEvaluator.FilterByRanges(plan.IndexRanges, encoded);
            var sorted = indexCandidates
                .OrderBy(candidate => candidate.Morton)
                .ThenBy(candidate => candidate.Index)
                .Select(candidate => candidate.Index)
                .ToArray();

            if (iteration == 0)
            {
                baseline = sorted;
                continue;
            }

            sorted.Should().Equal(baseline);
        }
    }
}
