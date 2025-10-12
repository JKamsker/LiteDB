extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
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

    public static IEnumerable<object[]> Fixtures => Cartesian3DLatticeFixtureLoader.FixtureData();

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void NearQueriesMatchMathNet(Cartesian3DLatticeFixture fixture)
    {
        using var context = FixtureContext.Create(fixture);

        foreach (var query in fixture.NearQueries)
        {
            var plan = SpatialCartesian3D.Near(context.Descriptor, query.Center, query.Radius);
            LogCovering("near", fixture.Name, query.Id, plan);

            if (query.ExpectCapped)
            {
                plan.Covering.WasCapped.Should().BeTrue($"Fixture {fixture.Name} query {query.Id} should trigger MaxCoveringCells fallback.");
            }

            var tolerance = context.Descriptor.Options.DistanceTolerance;
            var expected = fixture.Points
                .Select(point => new
                {
                    point.Id,
                    Distance = MathNetOracle3D.Distance((point.X, point.Y, point.Z), (query.Center.X, query.Center.Y, query.Center.Z))
                })
                .Where(candidate => candidate.Distance <= query.Radius + tolerance + 1e-9)
                .OrderBy(candidate => candidate.Distance)
                .ToList();

            var actual = Spatial.Near(context.Collection, x => x.Position, query.Center, query.Radius);
            var actualProjected = actual
                .Select(item => new
                {
                    item.Id,
                    Distance = MathNetOracle3D.Distance((item.Position.X, item.Position.Y, item.Position.Z), (query.Center.X, query.Center.Y, query.Center.Z))
                })
                .ToList();

            try
            {
                actualProjected.Select(x => x.Id).Should().Equal(expected.Select(x => x.Id));
                actualProjected.Should().AllSatisfy(result => result.Distance.Should().BeLessThanOrEqualTo(query.Radius + tolerance + 1e-9));
            }
            catch
            {
                DifferentialFailureRecorder.Record(
                    fixture.Name,
                    query.Id,
                    new
                    {
                        fixture = fixture.Name,
                        query = query.Id,
                        queryCenter = new { query.Center.X, query.Center.Y, query.Center.Z },
                        queryRadius = query.Radius,
                        tolerance,
                        covering = new
                        {
                            estimated = plan.Covering.EstimatedCellCount,
                            requested = plan.Covering.RequestedMaxCells,
                            original = plan.Covering.OriginalRangeCount,
                            final = plan.Covering.FinalRangeCount,
                            wasCapped = plan.Covering.WasCapped
                        },
                        expected = expected,
                        actual = actualProjected,
                        points = fixture.Points
                    });
                throw;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void BoundingBoxQueriesMatchManualPredicates(Cartesian3DLatticeFixture fixture)
    {
        using var context = FixtureContext.Create(fixture);

        foreach (var query in fixture.AabbQueries)
        {
            var plan = SpatialCartesian3D.WithinBoundingBox(context.Descriptor, query.Bounds);
            LogCovering("aabb", fixture.Name, query.Id, plan);

            var expected = fixture.Points
                .Where(point => Contains(query.Bounds, point))
                .Select(point => point.Id)
                .OrderBy(id => id)
                .ToList();

            var actual = Spatial.WithinBoundingBox(context.Collection, x => x.Position, query.Bounds)
                .Select(point => point.Id)
                .OrderBy(id => id)
                .ToList();

            try
            {
                actual.Should().Equal(expected);
                VerifyBoundingBoxesAreNormalized(context, fixture);
            }
            catch
            {
                DifferentialFailureRecorder.Record(
                    fixture.Name,
                    query.Id,
                    new
                    {
                        fixture = fixture.Name,
                        query = query.Id,
                        bounds = query.Bounds.ToArray(),
                        covering = new
                        {
                            estimated = plan.Covering.EstimatedCellCount,
                            requested = plan.Covering.RequestedMaxCells,
                            original = plan.Covering.OriginalRangeCount,
                            final = plan.Covering.FinalRangeCount,
                            wasCapped = plan.Covering.WasCapped
                        },
                        expected,
                        actual,
                        points = fixture.Points
                    });
                throw;
            }
        }
    }

    private void VerifyBoundingBoxesAreNormalized(FixtureContext context, Cartesian3DLatticeFixture fixture)
    {
        var raw = context.Database.GetCollection<BaseLiteDB.BsonDocument>(context.Collection.Name);
        var fieldName = context.Descriptor.Options.BoundingBoxFieldName;

        foreach (var document in raw.FindAll())
        {
            document.TryGetValue("_id", out var idValue).Should().BeTrue();
            var id = idValue.AsInt32;
            fixture.Points.Should().Contain(point => point.Id == id);

            document.TryGetValue(fieldName, out var boundingValue).Should().BeTrue();
            var array = boundingValue.AsArray;
            array.Count.Should().Be(6, "3D _mbb arrays should contain six values.");

            var minX = array[0].AsDouble;
            var minY = array[1].AsDouble;
            var minZ = array[2].AsDouble;
            var maxX = array[3].AsDouble;
            var maxY = array[4].AsDouble;
            var maxZ = array[5].AsDouble;

            minX.Should().BeLessOrEqualTo(maxX);
            minY.Should().BeLessOrEqualTo(maxY);
            minZ.Should().BeLessOrEqualTo(maxZ);

            var source = fixture.Points.Single(point => point.Id == id);
            minX.Should().BeApproximately(source.X, 1e-6);
            minY.Should().BeApproximately(source.Y, 1e-6);
            minZ.Should().BeApproximately(source.Z, 1e-6);
            maxX.Should().BeApproximately(source.X, 1e-6);
            maxY.Should().BeApproximately(source.Y, 1e-6);
            maxZ.Should().BeApproximately(source.Z, 1e-6);
        }
    }

    private static bool Contains(BoundingBox bounds, Cartesian3DLatticePoint point)
    {
        var values = bounds.GetValues();
        return point.X >= values[0] && point.Y >= values[1] && point.Z >= values[2]
            && point.X <= values[3] && point.Y <= values[4] && point.Z <= values[5];
    }

    private void LogCovering(string category, string fixtureName, string queryId, ISpatialQueryPlan plan)
    {
        _output.WriteLine(
            $"[{category}] {fixtureName}/{queryId}: ranges={plan.Covering.FinalRangeCount}/{plan.Covering.RequestedMaxCells} capped={plan.Covering.WasCapped} estimated={plan.Covering.EstimatedCellCount} original={plan.Covering.OriginalRangeCount}");
    }

    private sealed class FixtureContext : IDisposable
    {
        private FixtureContext(BaseLiteDB.LiteDatabase database, BaseLiteDB.ILiteCollection<TestPoint> collection, SpatialCollectionDescriptor descriptor, IReadOnlyList<TestPoint> points)
        {
            Database = database;
            Collection = collection;
            Descriptor = descriptor;
            Points = points;
        }

        public BaseLiteDB.LiteDatabase Database { get; }

        public BaseLiteDB.ILiteCollection<TestPoint> Collection { get; }

        public SpatialCollectionDescriptor Descriptor { get; }

        public IReadOnlyList<TestPoint> Points { get; }

        public static FixtureContext Create(Cartesian3DLatticeFixture fixture)
        {
            var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
            database.Mapper.Entity<TestPoint>().Id(x => x.Id, autoId: false);
            var collection = database.GetCollection<TestPoint>($"fixture_{fixture.Name}");
            var descriptor = Spatial.UseCartesian3D(collection, x => x.Position, fixture.Domain, fixture.Options);

            collection.DeleteAll();
            var documents = fixture.Points
                .Select(point => new TestPoint
                {
                    Id = point.Id,
                    Position = point.ToGeoPoint()
                })
                .ToList();

            collection.Insert(documents);
            descriptor = Spatial.EnsurePointIndex(collection);

            return new FixtureContext(database, collection, descriptor, documents);
        }

        public void Dispose()
        {
            Database.Dispose();
        }
    }

    private sealed class TestPoint
    {
        public int Id { get; set; }

        public GeoPoint3D Position { get; set; } = default!;
    }
}
