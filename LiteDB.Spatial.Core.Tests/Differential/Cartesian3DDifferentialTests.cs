extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential;

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
        var fixture = LoadFixture("lattice_v1.json");
        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<BaseLiteDB.BsonDocument>("cartesian3d_fixture");
        var metadata = new SpatialMetadataStore(database);

        foreach (var point in fixture.Points)
        {
            collection.Insert(new BaseLiteDB.BsonDocument
            {
                ["_id"] = point.Id,
                [fixture.GeometryField] = new BaseLiteDB.BsonDocument
                {
                    ["x"] = point.X,
                    ["y"] = point.Y,
                    ["z"] = point.Z
                }
            });
        }

        var domain = BoundingBox.From3D(
            fixture.Domain.Min.X,
            fixture.Domain.Min.Y,
            fixture.Domain.Min.Z,
            fixture.Domain.Max.X,
            fixture.Domain.Max.Y,
            fixture.Domain.Max.Z);

        var options = new SpatialIndexOptions(precisionBits: 12, maxCoveringCells: 4, distanceTolerance: 1e-9);
        var descriptor = SpatialCartesian3D.EnsurePointIndex(metadata, collection, fixture.GeometryField, domain, options);

        var clippedSeen = false;

        foreach (var query in fixture.NearQueries)
        {
            var center = new GeoPoint3D(query.Center.X, query.Center.Y, query.Center.Z);
            var plan = SpatialCartesian3D.Near(descriptor, center, query.Radius);

            var actual = ExecuteNearPlan(collection, descriptor, plan, center, query.Radius)
                .OrderBy(r => r.Id, StringComparer.Ordinal)
                .ToList();
            var expected = ComputeOracleResults(fixture.Points, center, query.Radius, descriptor.Options.DistanceTolerance)
                .OrderBy(r => r.Id, StringComparer.Ordinal)
                .ToList();

            clippedSeen |= plan.CoveringMetrics.WasClippedByMaxCells;
            _output.WriteLine(
                "Fixture {0} query {1}: cells={2} ranges={3} clipped={4}",
                fixture.Id,
                query.Id,
                plan.CoveringMetrics.CellCountEstimate,
                plan.CoveringMetrics.RangeCount,
                plan.CoveringMetrics.WasClippedByMaxCells);

            var expectedIds = expected.Select(r => r.Id).ToList();
            var actualIds = actual.Select(r => r.Id).ToList();

            if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
            {
                WriteFailure(
                    fixture.Id,
                    query.Id,
                    plan,
                    center,
                    query.Radius,
                    expected,
                    actual,
                    reason: "membership");
            }

            actualIds.Should().Equal(expectedIds, "query results should match oracle membership");

            var tolerance = CalculateTolerance(expected);
            var deltas = ComputeDistanceDeltas(actual, expected).ToList();
            var worst = deltas.Count == 0 ? 0d : deltas.Max();

            if (worst > tolerance)
            {
                WriteFailure(
                    fixture.Id,
                    query.Id,
                    plan,
                    center,
                    query.Radius,
                    expected,
                    actual,
                    reason: "distance");
            }

            worst.Should().BeLessOrEqualTo(tolerance, "distance deltas should stay within tolerance");
        }

        ValidateBoundingBoxes(collection, descriptor);
        ValidateBoundingBoxQueries(fixture, collection, descriptor, ref clippedSeen);

        clippedSeen.Should().BeTrue("fixtures include cases that exceed the covering budget");
    }

    private static IReadOnlyList<QueryResult> ExecuteNearPlan(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        SpatialCollectionDescriptor descriptor,
        ISpatialQueryPlan plan,
        GeoPoint3D center,
        double radius)
    {
        var results = new List<QueryResult>();
        var tolerance = descriptor.Options.DistanceTolerance;

        foreach (var document in collection.FindAll())
        {
            if (!document.TryGetValue(descriptor.Options.IndexFieldName, out var indexValue) ||
                !TryConvertIndex(indexValue, out var index))
            {
                continue;
            }

            if (!IsWithinRanges(index, plan.IndexRanges))
            {
                continue;
            }

            if (!PassesBoundingPrefilter(document, descriptor, plan.CoveringBounds))
            {
                continue;
            }

            if (!TryReadPoint(document, descriptor.GeometryFieldName, out var point))
            {
                continue;
            }

            var distance = MathNetOracle3D.Distance(center, point);
            if (distance <= radius + tolerance)
            {
                results.Add(new QueryResult(document["_id"].AsString, point, distance));
            }
        }

        return results;
    }

    private static IReadOnlyList<QueryResult> ExecuteBoundingBoxPlan(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        SpatialCollectionDescriptor descriptor,
        ISpatialQueryPlan plan)
    {
        var results = new List<QueryResult>();

        foreach (var document in collection.FindAll())
        {
            if (!document.TryGetValue(descriptor.Options.IndexFieldName, out var indexValue) ||
                !TryConvertIndex(indexValue, out var index))
            {
                continue;
            }

            if (!IsWithinRanges(index, plan.IndexRanges))
            {
                continue;
            }

            if (!PassesBoundingPrefilter(document, descriptor, plan.CoveringBounds))
            {
                continue;
            }

            if (!TryReadPoint(document, descriptor.GeometryFieldName, out var point))
            {
                continue;
            }

            results.Add(new QueryResult(document["_id"].AsString, point, 0));
        }

        return results;
    }

    private static bool PassesBoundingPrefilter(
        BaseLiteDB.BsonDocument document,
        SpatialCollectionDescriptor descriptor,
        BoundingBox? coveringBounds)
    {
        if (coveringBounds is null)
        {
            return true;
        }

        if (!document.TryGetValue(descriptor.Options.BoundingBoxFieldName, out var boundingValue) || !boundingValue.IsArray)
        {
            return false;
        }

        var array = boundingValue.AsArray;
        if (array.Count != descriptor.ExpectedBoundingBoxLength)
        {
            return false;
        }

        var dims = descriptor.Dimensions;
        const double epsilon = 1e-9;

        for (var axis = 0; axis < dims; axis++)
        {
            var min = array[axis].AsDouble;
            var max = array[axis + dims].AsDouble;
            double coverMin;
            double coverMax;

            if (axis == 0)
            {
                coverMin = coveringBounds.Value.MinX;
                coverMax = coveringBounds.Value.MaxX;
            }
            else if (axis == 1)
            {
                coverMin = coveringBounds.Value.MinY;
                coverMax = coveringBounds.Value.MaxY;
            }
            else
            {
                coverMin = coveringBounds.Value.MinZ;
                coverMax = coveringBounds.Value.MaxZ;
            }

            if (max < coverMin - epsilon || min > coverMax + epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadPoint(BaseLiteDB.BsonDocument document, string fieldName, out GeoPoint3D point)
    {
        point = default;

        if (!document.TryGetValue(fieldName, out var geometry) || !geometry.IsDocument)
        {
            return false;
        }

        var geoDoc = geometry.AsDocument;
        if (!geoDoc.TryGetValue("x", out var x) ||
            !geoDoc.TryGetValue("y", out var y) ||
            !geoDoc.TryGetValue("z", out var z))
        {
            return false;
        }

        point = new GeoPoint3D(x.AsDouble, y.AsDouble, z.AsDouble);
        return true;
    }

    private static bool IsWithinRanges(ulong index, IReadOnlyList<SpatialIndexRange> ranges)
    {
        foreach (var range in ranges)
        {
            if (index >= range.Start && index <= range.End)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<QueryResult> ComputeOracleResults(
        IReadOnlyList<PointFixture> points,
        GeoPoint3D center,
        double radius,
        double tolerance)
    {
        var results = new List<QueryResult>();

        foreach (var point in points)
        {
            var geometry = new GeoPoint3D(point.X, point.Y, point.Z);
            var distance = MathNetOracle3D.Distance(center, geometry);
            if (distance <= radius + tolerance)
            {
                results.Add(new QueryResult(point.Id, geometry, distance));
            }
        }

        return results;
    }

    private static double CalculateTolerance(IEnumerable<QueryResult> results)
    {
        var scale = results.Select(r => Math.Abs(r.Distance)).DefaultIfEmpty(1d).Max();
        return (1e-9 * scale) + 1e-9;
    }

    private static IEnumerable<double> ComputeDistanceDeltas(
        IReadOnlyList<QueryResult> actual,
        IReadOnlyList<QueryResult> expected)
    {
        var expectedById = expected.ToDictionary(r => r.Id, r => r.Distance, StringComparer.Ordinal);
        foreach (var result in actual)
        {
            if (expectedById.TryGetValue(result.Id, out var reference))
            {
                yield return Math.Abs(result.Distance - reference);
            }
        }
    }

    private static void ValidateBoundingBoxes(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        SpatialCollectionDescriptor descriptor)
    {
        var expectedLength = descriptor.ExpectedBoundingBoxLength;

        foreach (var document in collection.FindAll())
        {
            document.TryGetValue(descriptor.Options.BoundingBoxFieldName, out var boundingValue).Should().BeTrue();
            boundingValue!.IsArray.Should().BeTrue();
            var array = boundingValue.AsArray;
            array.Count.Should().Be(expectedLength);

            var dims = descriptor.Dimensions;
            for (var axis = 0; axis < dims; axis++)
            {
                var min = array[axis].AsDouble;
                var max = array[axis + dims].AsDouble;
                min.Should().BeLessOrEqualTo(max + 1e-12, "bounding boxes must be normalized");
            }
        }
    }

    private void ValidateBoundingBoxQueries(
        LatticeFixture fixture,
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        SpatialCollectionDescriptor descriptor,
        ref bool clippedSeen)
    {
        foreach (var box in fixture.BoundingBoxes)
        {
            var bounds = BoundingBox.From3D(
                box.Min.X,
                box.Min.Y,
                box.Min.Z,
                box.Max.X,
                box.Max.Y,
                box.Max.Z);

            var plan = SpatialCartesian3D.WithinBoundingBox(descriptor, bounds);
            clippedSeen |= plan.CoveringMetrics.WasClippedByMaxCells;
            _output.WriteLine(
                "Fixture {0} bounding {1}: cells={2} ranges={3} clipped={4}",
                fixture.Id,
                box.Id,
                plan.CoveringMetrics.CellCountEstimate,
                plan.CoveringMetrics.RangeCount,
                plan.CoveringMetrics.WasClippedByMaxCells);

            var actual = ExecuteBoundingBoxPlan(collection, descriptor, plan)
                .Select(r => r.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            var expected = fixture.Points
                .Where(point => Contains(box, point))
                .Select(point => point.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                WriteFailure(
                    fixture.Id,
                    box.Id,
                    plan,
                    new GeoPoint3D(bounds.MinX, bounds.MinY, bounds.MinZ),
                    radius: 0,
                    expected.Select(id => new QueryResult(id, default, 0)).ToList(),
                    actual.Select(id => new QueryResult(id, default, 0)).ToList(),
                    reason: "bounding-box");
            }

            actual.Should().Equal(expected, "manual inequalities should match bounding box planner");
        }
    }

    private static bool Contains(BoundingBoxFixture box, PointFixture point)
    {
        return point.X >= box.Min.X && point.X <= box.Max.X
            && point.Y >= box.Min.Y && point.Y <= box.Max.Y
            && point.Z >= box.Min.Z && point.Z <= box.Max.Z;
    }

    private static bool TryConvertIndex(BaseLiteDB.BsonValue value, out ulong index)
    {
        switch (value.Type)
        {
            case BaseLiteDB.BsonType.Int32:
                var intValue = value.AsInt32;
                if (intValue < 0)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)intValue;
                return true;
            case BaseLiteDB.BsonType.Int64:
                var longValue = value.AsInt64;
                if (longValue < 0)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)longValue;
                return true;
            case BaseLiteDB.BsonType.Decimal:
                var decimalValue = value.AsDecimal;
                if (decimalValue < 0 || decimalValue > ulong.MaxValue)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)decimalValue;
                return decimalValue == (decimal)index;
            case BaseLiteDB.BsonType.Double:
                var doubleValue = value.AsDouble;
                if (doubleValue < 0 || doubleValue > ulong.MaxValue)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)doubleValue;
                return Math.Abs(doubleValue - index) < 0.5d;
            default:
                index = 0;
                return false;
        }
    }

    private static void WriteFailure(
        string fixtureId,
        string queryId,
        ISpatialQueryPlan plan,
        GeoPoint3D center,
        double radius,
        IReadOnlyList<QueryResult> expected,
        IReadOnlyList<QueryResult> actual,
        string reason)
    {
        var root = GetRepositoryRoot();
        var directory = Path.Combine(root, "tests", "failures", "3d");
        Directory.CreateDirectory(directory);
        var fileName = $"{fixtureId}-{queryId}-{reason}.json";
        var path = Path.Combine(directory, fileName);

        var payload = new FailureRecord
        {
            FixtureId = fixtureId,
            QueryId = queryId,
            Reason = reason,
            Center = new SerializablePoint(center.X, center.Y, center.Z),
            Radius = radius,
            Tolerance = CalculateTolerance(expected),
                Covering = new CoveringSnapshot
                {
                    CellCount = plan.CoveringMetrics.CellCountEstimate,
                    RangeCount = plan.CoveringMetrics.RangeCount,
                    MaxCoveringCells = plan.CoveringMetrics.RequestedMaxCoveringCells,
                    WasClipped = plan.CoveringMetrics.WasClippedByMaxCells
                },
            Expected = expected.Select(e => new FailureResult(e.Id, e.Point.X, e.Point.Y, e.Point.Z, e.Distance)).ToList(),
            Actual = actual.Select(e => new FailureResult(e.Id, e.Point.X, e.Point.Y, e.Point.Z, e.Distance)).ToList()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string GetRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(current, "..", "..", "..", ".."));
    }

    private static LatticeFixture LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "cartesian3d", fileName);
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var fixture = JsonSerializer.Deserialize<LatticeFixture>(json, options);
        fixture.Should().NotBeNull();
        return fixture!;
    }

    private sealed record QueryResult(string Id, GeoPoint3D Point, double Distance);

    private sealed record FailureRecord
    {
        public string FixtureId { get; init; } = string.Empty;
        public string QueryId { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public SerializablePoint Center { get; init; } = new SerializablePoint();
        public double Radius { get; init; }
        public double Tolerance { get; init; }
        public CoveringSnapshot Covering { get; init; } = new CoveringSnapshot();
        public List<FailureResult> Expected { get; init; } = new();
        public List<FailureResult> Actual { get; init; } = new();
    }

    private sealed record CoveringSnapshot
    {
        public ulong CellCount { get; init; }
        public int RangeCount { get; init; }
        public int MaxCoveringCells { get; init; }
        public bool WasClipped { get; init; }
    }

    private sealed record FailureResult(string Id, double X, double Y, double Z, double Distance);

    private sealed record SerializablePoint
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }

        public SerializablePoint()
        {
        }

        public SerializablePoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    private sealed record LatticeFixture
    {
        public string Id { get; init; } = string.Empty;
        public BoundingBoxFixture Domain { get; init; } = new BoundingBoxFixture();
        public List<PointFixture> Points { get; init; } = new();
        public List<NearQuery> NearQueries { get; init; } = new();
        public List<BoundingBoxFixture> BoundingBoxes { get; init; } = new();
        public string GeometryField { get; init; } = "position";
    }

    private sealed record BoundingBoxFixture
    {
        public string Id { get; init; } = string.Empty;
        public Point3D Min { get; init; } = new Point3D();
        public Point3D Max { get; init; } = new Point3D();
    }

    private sealed record Point3D
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
    }

    private sealed record PointFixture
    {
        public string Id { get; init; } = string.Empty;
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
    }

    private sealed record NearQuery
    {
        public string Id { get; init; } = string.Empty;
        public Point3D Center { get; init; } = new Point3D();
        public double Radius { get; init; }
    }

    private static class MathNetOracle3D
    {
        public static double Distance(GeoPoint3D a, GeoPoint3D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }
}
