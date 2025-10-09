extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LiteDB.Spatial;
using Xunit;
using Xunit.Abstractions;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

public sealed class Cartesian3DDifferentialTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Differential", "Cartesian3D", "Fixtures");
    private static readonly string FailureDirectory = Path.Combine(GetRepositoryRoot(), "tests", "failures", "3d");
    private readonly ITestOutputHelperAdapter _output;

    public Cartesian3DDifferentialTests(ITestOutputHelper output)
    {
        _output = new TestOutputHelperAdapter(output);
    }

    public static IEnumerable<object[]> LatticeFixtures()
    {
        if (!Directory.Exists(FixtureDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(FixtureDirectory, "*.json"))
        {
            var json = File.ReadAllText(file);
            var fixture = JsonSerializer.Deserialize<Cartesian3DFixture>(json, JsonOptions);
            if (fixture is null)
            {
                continue;
            }

            yield return new object[] { Path.GetFileNameWithoutExtension(file) ?? fixture.Name, fixture };
        }
    }

    [Theory]
    [MemberData(nameof(LatticeFixtures))]
    public void NearQueriesMatchMathNet(string fixtureName, Cartesian3DFixture fixture)
    {
        using var context = CreateContext(fixture);

        foreach (var query in fixture.Queries)
        {
            var center = ToGeoPoint(query.Center);
            var plan = SpatialCartesian3D.Near(context.Descriptor, center, query.Radius);
            _output.WriteLine($"[{fixtureName}/{query.Name}] Covering cells={plan.CoveringCellCount}, fallback={plan.UsedMaxCoveringCellFallback}");

            plan.UsedMaxCoveringCellFallback.Should().Be(query.ExpectFallback);
            var maxCoveringCells = context.Descriptor.Options.MaxCoveringCells;
            plan.IndexRanges.Count.Should().BeLessOrEqualTo(maxCoveringCells);
            if (plan.UsedMaxCoveringCellFallback)
            {
                plan.CoveringCellCount.Should().BeGreaterThan(maxCoveringCells);
            }
            else
            {
                plan.CoveringCellCount.Should().BeLessOrEqualTo(maxCoveringCells);
            }

            var results = Spatial.Near(context.Collection, x => x.Position, center, query.Radius).ToList();
            var actualIds = results.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

            var tolerance = query.Tolerance ?? context.Descriptor.Options.DistanceTolerance;
            var expectedIds = fixture.Points
                .Where(p => MathNetOracle3D.Distance(p.Coordinates, query.Center) <= query.Radius + tolerance)
                .Select(p => p.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            try
            {
                actualIds.Should().Equal(expectedIds);

                foreach (var id in actualIds)
                {
                    var distance = MathNetOracle3D.Distance(context.Points[id].Coordinates, query.Center);
                    distance.Should().BeLessOrEqualTo(query.Radius + tolerance + 1e-9, $"Point {id} should respect the tolerance window");
                }
            }
            catch (Exception ex)
            {
                RecordFailure(fixtureName, $"near-{query.Name}", new NearFailure(
                    fixture.Name,
                    query,
                    context.Descriptor.Options,
                    plan.CoveringCellCount,
                    plan.UsedMaxCoveringCellFallback,
                    expectedIds,
                    actualIds,
                    fixture.Points), ex);
                throw;
            }
        }
    }

    [Theory]
    [MemberData(nameof(LatticeFixtures))]
    public void BoundingBoxesMatchManualPredicates(string fixtureName, Cartesian3DFixture fixture)
    {
        using var context = CreateContext(fixture);

        var raw = context.Database.GetCollection("points");
        var docs = raw.FindAll().ToList();

        foreach (var doc in docs)
        {
            var mbb = doc[context.Descriptor.Options.BoundingBoxFieldName].AsArray;
            mbb.Count.Should().Be(6);

            var minX = mbb[0].AsDouble;
            var maxX = mbb[3].AsDouble;
            var minY = mbb[1].AsDouble;
            var maxY = mbb[4].AsDouble;
            var minZ = mbb[2].AsDouble;
            var maxZ = mbb[5].AsDouble;

            minX.Should().BeLessOrEqualTo(maxX + 1e-9);
            minY.Should().BeLessOrEqualTo(maxY + 1e-9);
            minZ.Should().BeLessOrEqualTo(maxZ + 1e-9);
        }

        foreach (var box in fixture.Boxes)
        {
            var bounds = BoundingBox.From3D(box.Min[0], box.Min[1], box.Min[2], box.Max[0], box.Max[1], box.Max[2]);
            var plan = SpatialCartesian3D.WithinBoundingBox(context.Descriptor, bounds);
            _output.WriteLine($"[{fixtureName}/{box.Name}] Covering cells={plan.CoveringCellCount}, fallback={plan.UsedMaxCoveringCellFallback}");

            plan.UsedMaxCoveringCellFallback.Should().Be(box.ExpectFallback);
            var maxCoveringCells = context.Descriptor.Options.MaxCoveringCells;
            plan.IndexRanges.Count.Should().BeLessOrEqualTo(maxCoveringCells);
            if (plan.UsedMaxCoveringCellFallback)
            {
                plan.CoveringCellCount.Should().BeGreaterThan(maxCoveringCells);
            }
            else
            {
                plan.CoveringCellCount.Should().BeLessOrEqualTo(maxCoveringCells);
            }

            var results = Spatial.WithinBoundingBox(context.Collection, x => x.Position, bounds)
                .Select(p => p.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            var expected = fixture.Points
                .Where(p => IsInside(bounds, p.Coordinates))
                .Select(p => p.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            try
            {
                results.Should().Equal(expected);
            }
            catch (Exception ex)
            {
                RecordFailure(fixtureName, $"aabb-{box.Name}", new BoundingFailure(
                    fixture.Name,
                    box,
                    context.Descriptor.Options,
                    bounds,
                    plan.CoveringCellCount,
                    plan.UsedMaxCoveringCellFallback,
                    expected,
                    results,
                    fixture.Points), ex);
                throw;
            }
        }
    }

    private FixtureContext CreateContext(Cartesian3DFixture fixture)
    {
        var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<FixturePoint>("points");
        var domain = BoundingBox.From3D(
            fixture.Domain.Min[0],
            fixture.Domain.Min[1],
            fixture.Domain.Min[2],
            fixture.Domain.Max[0],
            fixture.Domain.Max[1],
            fixture.Domain.Max[2]);
        var options = new SpatialIndexOptions(
            fixture.Options.PrecisionBits,
            fixture.Options.MaxCoveringCells,
            fixture.Options.DistanceTolerance);

        var descriptor = Spatial.UseCartesian3D(collection, x => x.Position, domain, options);
        var entities = fixture.Points
            .Select(p => new FixturePoint(p.Id, new GeoPoint3D(p.Coordinates[0], p.Coordinates[1], p.Coordinates[2])))
            .ToList();

        collection.Insert(entities);
        Spatial.EnsurePointIndex(collection);

        return new FixtureContext(database, collection, descriptor, fixture);
    }

    private static GeoPoint3D ToGeoPoint(double[] coordinates)
    {
        return new GeoPoint3D(coordinates[0], coordinates[1], coordinates[2]);
    }

    private static bool IsInside(BoundingBox bounds, double[] coordinates)
    {
        return coordinates[0] >= bounds.MinX - 1e-9 && coordinates[0] <= bounds.MaxX + 1e-9
            && coordinates[1] >= bounds.MinY - 1e-9 && coordinates[1] <= bounds.MaxY + 1e-9
            && coordinates[2] >= bounds.MinZ - 1e-9 && coordinates[2] <= bounds.MaxZ + 1e-9;
    }

    private static void RecordFailure(string fixtureName, string scenario, object payload, Exception exception)
    {
        Directory.CreateDirectory(FailureDirectory);
        var fileName = $"{Sanitize(fixtureName)}-{Sanitize(scenario)}.json";
        var path = Path.Combine(FailureDirectory, fileName);
        var json = JsonSerializer.Serialize(new FailureEnvelope(payload, exception.ToString()), new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private static string Sanitize(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private sealed class FixtureContext : IDisposable
    {
        public FixtureContext(BaseLiteDB.LiteDatabase database, BaseLiteDB.ILiteCollection<FixturePoint> collection, SpatialCollectionDescriptor descriptor, Cartesian3DFixture fixture)
        {
            Database = database;
            Collection = collection;
            Descriptor = descriptor;
            Fixture = fixture;
            Points = fixture.Points.ToDictionary(p => p.Id, StringComparer.Ordinal);
        }

        public BaseLiteDB.LiteDatabase Database { get; }
        public BaseLiteDB.ILiteCollection<FixturePoint> Collection { get; }
        public SpatialCollectionDescriptor Descriptor { get; }
        public Cartesian3DFixture Fixture { get; }
        public IReadOnlyDictionary<string, LatticePointSpec> Points { get; }

        public void Dispose()
        {
            Database.Dispose();
        }
    }

    private sealed record FixturePoint(string Id, GeoPoint3D Position);

    public sealed record Cartesian3DFixture(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("domain")] DomainSpec Domain,
        [property: JsonPropertyName("options")] OptionsSpec Options,
        [property: JsonPropertyName("points")] IReadOnlyList<LatticePointSpec> Points,
        [property: JsonPropertyName("queries")] IReadOnlyList<LatticeQuerySpec> Queries,
        [property: JsonPropertyName("boxes")] IReadOnlyList<LatticeBoundingBoxSpec> Boxes);

    public sealed record DomainSpec(
        [property: JsonPropertyName("min")] double[] Min,
        [property: JsonPropertyName("max")] double[] Max);

    public sealed record OptionsSpec(
        [property: JsonPropertyName("precisionBits")] int PrecisionBits,
        [property: JsonPropertyName("maxCoveringCells")] int MaxCoveringCells,
        [property: JsonPropertyName("distanceTolerance")] double DistanceTolerance);

    public sealed record LatticePointSpec(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("coordinates")] double[] Coordinates);

    public sealed record LatticeQuerySpec(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("center")] double[] Center,
        [property: JsonPropertyName("radius")] double Radius,
        [property: JsonPropertyName("tolerance")] double? Tolerance,
        [property: JsonPropertyName("expectFallback")] bool ExpectFallback);

    public sealed record LatticeBoundingBoxSpec(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("min")] double[] Min,
        [property: JsonPropertyName("max")] double[] Max,
        [property: JsonPropertyName("expectFallback")] bool ExpectFallback);

    private sealed record NearFailure(
        string Fixture,
        LatticeQuerySpec Query,
        SpatialIndexOptions Options,
        int CoveringCells,
        bool UsedFallback,
        IReadOnlyList<string> Expected,
        IReadOnlyList<string> Actual,
        IReadOnlyList<LatticePointSpec> Points);

    private sealed record BoundingFailure(
        string Fixture,
        LatticeBoundingBoxSpec Box,
        SpatialIndexOptions Options,
        BoundingBox Bounds,
        int CoveringCells,
        bool UsedFallback,
        IReadOnlyList<string> Expected,
        IReadOnlyList<string> Actual,
        IReadOnlyList<LatticePointSpec> Points);

    private sealed record FailureEnvelope(object Data, string Exception);

    private interface ITestOutputHelperAdapter
    {
        void WriteLine(string message);
    }

    private sealed class TestOutputHelperAdapter : ITestOutputHelperAdapter
    {
        private readonly ITestOutputHelper _inner;

        public TestOutputHelperAdapter(ITestOutputHelper inner)
        {
            _inner = inner;
        }

        public void WriteLine(string message)
        {
            _inner.WriteLine(message);
        }
    }
}
