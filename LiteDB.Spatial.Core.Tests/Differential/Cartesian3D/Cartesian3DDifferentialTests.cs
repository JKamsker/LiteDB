extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests;
using LiteDB.Spatial.Core.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

[Category("differential")]
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

            EnsureExpectedFallback(plan.CoveringDiagnostics, fixture.Name, query);

            var baseTolerance = context.Descriptor.Options.DistanceTolerance;
            var expected = fixture.Points
                .Select(point => new
                {
                    point.Id,
                    Distance = MathNetOracle3D.Distance((point.X, point.Y, point.Z), (query.Center.X, query.Center.Y, query.Center.Z))
                })
                .Where(candidate => candidate.Distance <= query.Radius + baseTolerance + 1e-9)
                .OrderBy(candidate => candidate.Distance)
                .ToList();
            var membershipTolerance = CalculateMembershipTolerance(query.Radius, baseTolerance);
            var parityTolerance = CalculateParityTolerance(expected.Select(candidate => candidate.Distance), baseTolerance);

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

                var expectedById = expected.ToDictionary(entry => entry.Id, entry => entry.Distance);
                var deltas = actualProjected
                    .Where(entry => expectedById.TryGetValue(entry.Id, out _))
                    .Select(entry => Math.Abs(entry.Distance - expectedById[entry.Id]))
                    .ToList();

                actualProjected.Should().AllSatisfy(result =>
                    result.Distance.Should().BeLessThanOrEqualTo(query.Radius + membershipTolerance + 1e-9));
                if (deltas.Count > 0)
                {
                    deltas.Max().Should().BeLessOrEqualTo(parityTolerance + 1e-9, "distance deltas should remain within tolerance");
                }
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
                        membershipTolerance,
                        parityTolerance,
                        baseTolerance,
                        covering = CreateCoveringSnapshot(plan.CoveringDiagnostics, context.Descriptor.Options.MaxCoveringCells),
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
                        covering = CreateCoveringSnapshot(plan.CoveringDiagnostics, context.Descriptor.Options.MaxCoveringCells),
                        expected,
                        actual,
                        points = fixture.Points
                    });
                throw;
            }
        }
    }

    private static double CalculateMembershipTolerance(double radius, double baseTolerance)
    {
        var scaled = SpatialTolerance.ForCartesian(Math.Max(radius, baseTolerance));
        return Math.Max(baseTolerance, scaled);
    }

    private static double CalculateParityTolerance(IEnumerable<double> distances, double baseTolerance)
    {
        var scale = distances.DefaultIfEmpty(0d).Select(Math.Abs).DefaultIfEmpty(0d).Max();
        var scaled = scale <= 0d ? baseTolerance : SpatialTolerance.ForCartesian(scale);
        return Math.Max(baseTolerance, scaled);
    }

    private void VerifyBoundingBoxesAreNormalized(FixtureContext context, Cartesian3DLatticeFixture fixture)
    {
        var raw = context.Database.GetCollection<BaseLiteDB.BsonDocument>(context.Collection.Name);
        var fieldName = context.Descriptor.Options.BoundingBoxFieldName;

        foreach (var document in raw.FindAll())
        {
            document.TryGetValue("_id", out var idValue).Should().BeTrue();
            var id = idValue.Type switch
            {
                BaseLiteDB.BsonType.Int32 => idValue.AsInt32.ToString(CultureInfo.InvariantCulture),
                BaseLiteDB.BsonType.Int64 => idValue.AsInt64.ToString(CultureInfo.InvariantCulture),
                BaseLiteDB.BsonType.Double => idValue.AsDouble.ToString(CultureInfo.InvariantCulture),
                BaseLiteDB.BsonType.String => idValue.AsString,
                _ => idValue.ToString()
            };
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

    private static void EnsureExpectedFallback(SpatialCoveringDiagnostics diagnostics, string fixtureName, Cartesian3DNearQuery query)
    {
        if (query.ExpectCapped)
        {
            diagnostics.UsedMaxCoveringCellsFallback.Should().BeTrue(
                $"Fixture {fixtureName} query {query.Id} should trigger MaxCoveringCells fallback.");
        }
        else
        {
            diagnostics.UsedMaxCoveringCellsFallback.Should().BeFalse(
                $"Fixture {fixtureName} query {query.Id} should avoid MaxCoveringCells fallback.");
        }
    }

    private void LogCovering(string category, string fixtureName, string queryId, ISpatialQueryPlan plan)
    {
        var covering = plan.CoveringDiagnostics;
        _output.WriteLine(
            $"[{category}] {fixtureName}/{queryId}: requested={covering.RequestedRangeCount} returned={covering.ReturnedRangeCount} effective={covering.EffectiveRangeCount} estimated={covering.EstimatedCellCount} maxFallback={covering.UsedMaxCoveringCellsFallback} enumerationFallback={covering.UsedEnumerationFallback}");
    }

    private static object CreateCoveringSnapshot(SpatialCoveringDiagnostics covering, int maxCoveringCells)
    {
        return new
        {
            covering.RequestedRangeCount,
            covering.ReturnedRangeCount,
            covering.EffectiveRangeCount,
            covering.EstimatedCellCount,
            covering.UsedMaxCoveringCellsFallback,
            covering.UsedEnumerationFallback,
            maxCoveringCells
        };
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
            var collection = database.GetCollection<TestPoint>(BuildCollectionName(fixture.Name));
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

        private static string BuildCollectionName(string fixtureName)
        {
            var builder = new StringBuilder("fixture_");

            if (string.IsNullOrWhiteSpace(fixtureName))
            {
                builder.Append("cartesian3d");
                return builder.ToString();
            }

            foreach (var ch in fixtureName)
            {
                builder.Append(IsValidCollectionCharacter(ch) ? ch : '_');
            }

            return builder.ToString();
        }

        private static bool IsValidCollectionCharacter(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';
        }
    }

    private sealed class TestPoint
    {
        public string Id { get; set; } = default!;

        public GeoPoint3D Position { get; set; } = default!;
    }
}
