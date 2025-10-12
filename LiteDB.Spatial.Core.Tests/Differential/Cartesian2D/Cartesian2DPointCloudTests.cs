extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.Differential;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class Cartesian2DPointCloudTests
{
    private static readonly string FixturePath = RepositoryPath.Combine("tests", "fixtures", "point_clouds", "grid_3x3.json");

    [Fact]
    [Trait("Category", "Oracle")]
    public void NearDistancesRespectEuclideanExpectations()
    {
        var fixture = PointCloudFixture.Load(FixturePath);

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<CartesianDocument>("points");
        var liteCollection = (BaseLiteDB.LiteCollection<CartesianDocument>)collection;

        Spatial.UseCartesian2D(collection, x => x.Position, fixture.Domain.ToBoundingBox());
        collection.Insert(fixture.Points.Select(p => new CartesianDocument(p.Id, new GeoPoint(p.X, p.Y))));
        var descriptor = Spatial.EnsurePointIndex(collection);

        foreach (var query in fixture.Queries)
        {
            var center = new GeoPoint(query.Center.X, query.Center.Y);
            var plan = SpatialCartesian2D.Near(descriptor, center, query.Radius);
            var results = Spatial.Near(collection, x => x.Position, center, query.Radius);

            var ids = results.Select(r => r.Id).ToArray();
            ids.Should().BeEquivalentTo(query.Expected.ExactIds);

            foreach (var document in results)
            {
                var expectedDistance = Math.Sqrt(
                    Math.Pow(document.Position.Longitude - center.Longitude, 2) +
                    Math.Pow(document.Position.Latitude - center.Latitude, 2));

                var oracleDistance = NtsOracle.Distance(document.Position, center);
                var tolerance = SpatialTestTolerances.Cartesian(expectedDistance);

                oracleDistance.Should().BeApproximately(expectedDistance, tolerance);
                expectedDistance.Should().BeLessThanOrEqualTo(query.Radius + tolerance);
            }

            var counts = CandidateCounter.Measure(liteCollection, descriptor, plan, results.Count);
            counts.Index.Should().Be(query.Expected.IndexCandidates);
            counts.Prefilter.Should().Be(query.Expected.PrefilterCandidates);
            counts.Exact.Should().Be(query.Expected.ExactIds.Length);
            counts.Index.Should().BeGreaterOrEqualTo(counts.Prefilter);
            counts.Prefilter.Should().BeGreaterOrEqualTo(counts.Exact);
        }
    }

    [Fact]
    public void NearResultsRemainStableAcrossRuns()
    {
        var fixture = PointCloudFixture.Load(FixturePath);

        using var database = new BaseLiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<CartesianDocument>("points");

        Spatial.UseCartesian2D(collection, x => x.Position, fixture.Domain.ToBoundingBox());
        collection.Insert(fixture.Points.Select(p => new CartesianDocument(p.Id, new GeoPoint(p.X, p.Y))));
        Spatial.EnsurePointIndex(collection);

        var query = fixture.Queries.Single();
        var center = new GeoPoint(query.Center.X, query.Center.Y);

        var first = Spatial.Near(collection, x => x.Position, center, query.Radius);
        var second = Spatial.Near(collection, x => x.Position, center, query.Radius);

        first.Select(p => p.Id).Should().Equal(second.Select(p => p.Id));
    }

    private sealed record CartesianDocument(int Id, GeoPoint Position);

    private sealed record CandidateCounts(int Index, int Prefilter, int Exact);

    private static class CandidateCounter
    {
        public static CandidateCounts Measure(
            BaseLiteDB.LiteCollection<CartesianDocument> collection,
            SpatialCollectionDescriptor descriptor,
            ISpatialQueryPlan plan,
            int exactCount)
        {
            var indexExpression = BuildIndexExpression(descriptor, plan.IndexRanges);
            var indexCount = indexExpression == null ? collection.Count() : collection.Count(indexExpression);

            var combined = CombineExpressions(descriptor, plan, indexExpression);
            var prefilterCount = combined == null ? indexCount : collection.Count(combined);

            return new CandidateCounts(indexCount, prefilterCount, exactCount);
        }

        private static BaseLiteDB.BsonExpression? CombineExpressions(
            SpatialCollectionDescriptor descriptor,
            ISpatialQueryPlan plan,
            BaseLiteDB.BsonExpression? indexExpression)
        {
            if (plan.CoveringBounds is null)
            {
                return indexExpression;
            }

            var bounding = BuildBoundingExpression(descriptor, plan.CoveringBounds.Value);
            return Combine(indexExpression, bounding);
        }

        private static BaseLiteDB.BsonExpression? BuildIndexExpression(
            SpatialCollectionDescriptor descriptor,
            IReadOnlyList<SpatialIndexRange> ranges)
        {
            if (ranges.Count == 0)
            {
                return null;
            }

            var field = $"$.{descriptor.Options.IndexFieldName}";
            var expressions = new List<BaseLiteDB.BsonExpression>(ranges.Count);
            foreach (var range in ranges)
            {
                var start = CreateIndexValue(range.Start);
                var end = CreateIndexValue(range.End);
                expressions.Add(BaseLiteDB.Query.Between(field, start, end));
            }

            if (expressions.Count == 1)
            {
                return expressions[0];
            }

            return BaseLiteDB.Query.Or(expressions.ToArray());
        }

        private static BaseLiteDB.BsonExpression? BuildBoundingExpression(
            SpatialCollectionDescriptor descriptor,
            BoundingBox bounds)
        {
            var values = bounds.GetValues();
            var field = $"$.{descriptor.Options.BoundingBoxFieldName}";

            if (values.Length == 4)
            {
                var minX = new BaseLiteDB.BsonValue(values[0]);
                var minY = new BaseLiteDB.BsonValue(values[1]);
                var maxX = new BaseLiteDB.BsonValue(values[2]);
                var maxY = new BaseLiteDB.BsonValue(values[3]);

                var expression = $"({field} != null) AND {field}[0] <= {maxX} AND {field}[2] >= {minX} AND {field}[1] <= {maxY} AND {field}[3] >= {minY}";
                return BaseLiteDB.BsonExpression.Create(expression);
            }

            return null;
        }

        private static BaseLiteDB.BsonExpression? Combine(
            BaseLiteDB.BsonExpression? left,
            BaseLiteDB.BsonExpression? right)
        {
            if (left == null)
            {
                return right;
            }

            if (right == null)
            {
                return left;
            }

            var parameterValues = new List<BaseLiteDB.BsonValue>();
            var nextIndex = 0;

            static string Rewrite(
                BaseLiteDB.BsonExpression expression,
                List<BaseLiteDB.BsonValue> target,
                ref int index)
            {
                if (expression.Parameters == null || expression.Parameters.Count == 0)
                {
                    return expression.Source;
                }

                var map = new Dictionary<int, int>();
                foreach (var kvp in expression.Parameters)
                {
                    if (!int.TryParse(kvp.Key, out var original))
                    {
                        continue;
                    }

                    if (!map.ContainsKey(original))
                    {
                        map[original] = index++;
                        target.Add(kvp.Value);
                    }
                }

                if (map.Count == 0)
                {
                    return expression.Source;
                }

                var result = expression.Source;
                foreach (var (original, replacement) in map)
                {
                    result = result.Replace($"@{original}", $"@{replacement}");
                }

                return result;
            }

            var leftSource = Rewrite(left, parameterValues, ref nextIndex);
            var rightSource = Rewrite(right, parameterValues, ref nextIndex);

            return BaseLiteDB.BsonExpression.Create($"(({leftSource}) AND ({rightSource}))", parameterValues.ToArray());
        }

        private static BaseLiteDB.BsonValue CreateIndexValue(ulong value)
        {
            if (value <= long.MaxValue)
            {
                return new BaseLiteDB.BsonValue((long)value);
            }

            return new BaseLiteDB.BsonValue((decimal)value);
        }
    }

    private sealed record PointCloudFixture(
        string Name,
        PointCloudDomain Domain,
        IReadOnlyList<PointCloudPoint> Points,
        IReadOnlyList<PointCloudQuery> Queries)
    {
        public static PointCloudFixture Load(string path)
        {
            using var stream = File.OpenRead(path);
            var fixture = JsonSerializer.Deserialize<PointCloudFixture>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (fixture == null)
            {
                throw new InvalidOperationException($"Fixture '{path}' could not be deserialized.");
            }

            return fixture;
        }
    }

    private sealed record PointCloudDomain(double MinX, double MinY, double MaxX, double MaxY)
    {
        public BoundingBox ToBoundingBox() => BoundingBox.From2D(MinX, MinY, MaxX, MaxY);
    }

    private sealed record PointCloudPoint(int Id, double X, double Y);

    private sealed record PointCloudQuery(PointCloudCenter Center, double Radius, PointCloudExpectation Expected);

    private sealed record PointCloudCenter(double X, double Y);

    private sealed record PointCloudExpectation(int IndexCandidates, int PrefilterCandidates, int[] ExactIds);
}
