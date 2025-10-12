#nullable enable

extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;
using LiteDB.Spatial.Core.Tests.TestSupport;
using Xunit;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian2D;

public sealed class NearDiagnosticsTests
{
    public static IEnumerable<object[]> PointCloudQueries
    {
        get
        {
            foreach (var fixture in PointCloudFixtures.LoadAll())
            {
                foreach (var query in fixture.Queries)
                {
                    yield return new object[] { fixture.Name, query.Name };
                }
            }
        }
    }

    [Theory]
    [Trait("Category", "Oracle")]
    [MemberData(nameof(PointCloudQueries))]
    public void NearQueriesMatchEuclideanExpectations(string fixtureName, string queryName)
    {
        var fixture = PointCloudFixtures.Load(fixtureName);
        var query = fixture.Queries.Single(q => string.Equals(q.Name, queryName, StringComparison.Ordinal));

        using var harness = new Cartesian2DTestHarness(fixture.Domain);
        var documents = fixture.Points.Select(sample => new Cartesian2DTestHarness.CartesianPoint(sample.Id, sample.Position));
        harness.InsertPoints(documents);

        var plan = harness.PlanNear(query.Center, query.Radius);
        var metrics = SpatialPlanInspector.InspectNear(harness, query.Center, query.Radius);

        metrics.IndexCandidates.Should().BeGreaterOrEqualTo(metrics.CoveringCandidates);
        metrics.CoveringCandidates.Should().BeGreaterOrEqualTo(metrics.ExactMatches);

        var rawDocuments = harness.FetchRawDocuments();
        var indexExpected = CountIndexCandidates(rawDocuments, harness.Descriptor.Options.IndexFieldName, plan.IndexRanges);
        metrics.IndexCandidates.Should().Be(indexExpected);

        var coveringExpected = plan.CoveringBounds.HasValue
            ? fixture.Points.Count(sample => WithinBounds(sample.Position, plan.CoveringBounds.Value))
            : fixture.Points.Count;
        metrics.CoveringCandidates.Should().Be(coveringExpected);

        var expectedMatches = fixture.Points
            .Where(sample => EuclideanDistance(sample.Position, query.Center) <= query.Radius + SpatialTestTolerances.Distance)
            .Select(sample => sample.Id)
            .OrderBy(id => id)
            .ToList();

        var actualMatches = metrics.Results.Select(result => result.Id).OrderBy(id => id).ToList();
        actualMatches.Should().Equal(expectedMatches);

        foreach (var result in metrics.Results)
        {
            var distance = EuclideanDistance(result.Position, query.Center);
            distance.Should().BeLessOrEqualTo(query.Radius + SpatialTestTolerances.Distance);
        }
    }

    [Theory]
    [MemberData(nameof(PointCloudQueries))]
    [Trait("Category", "Oracle")]
    public void NearOrderingIsStable(string fixtureName, string queryName)
    {
        var fixture = PointCloudFixtures.Load(fixtureName);
        var query = fixture.Queries.Single(q => string.Equals(q.Name, queryName, StringComparison.Ordinal));

        using var harness = new Cartesian2DTestHarness(fixture.Domain);
        harness.InsertPoints(fixture.Points.Select(sample => new Cartesian2DTestHarness.CartesianPoint(sample.Id, sample.Position)));

        var baseline = SortByDistance(harness, fixture, query).ToList();
        baseline.Should().NotBeEmpty("fixtures should produce at least one match for diagnostics");

        for (var run = 0; run < 3; run++)
        {
            var rerun = SortByDistance(harness, fixture, query).ToList();
            rerun.Should().Equal(baseline);
        }
    }

    private static int CountIndexCandidates(
        IReadOnlyList<BaseLiteDB.BsonDocument> documents,
        string indexField,
        IReadOnlyList<SpatialIndexRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return documents.Count;
        }

        var fieldName = indexField;
        var count = 0;

        foreach (var document in documents)
        {
            if (!document.TryGetValue(fieldName, out var value))
            {
                continue;
            }

            if (!TryConvertToUInt64(value, out var encoded))
            {
                continue;
            }

            if (ranges.Any(range => encoded >= range.Start && encoded <= range.End))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryConvertToUInt64(BaseLiteDB.BsonValue value, out ulong result)
    {
        switch (value.Type)
        {
            case BaseLiteDB.BsonType.Int32:
                result = (ulong)(long)value.AsInt32;
                return true;
            case BaseLiteDB.BsonType.Int64:
                result = (ulong)value.AsInt64;
                return true;
            case BaseLiteDB.BsonType.Decimal:
                result = (ulong)value.AsDecimal;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool WithinBounds(GeoPoint point, BoundingBox bounds)
    {
        var values = bounds.GetValues();
        return point.Longitude >= values[0]
            && point.Longitude <= values[2]
            && point.Latitude >= values[1]
            && point.Latitude <= values[3];
    }

    private static double EuclideanDistance(GeoPoint point, GeoPoint center)
    {
        var deltaX = point.Longitude - center.Longitude;
        var deltaY = point.Latitude - center.Latitude;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static IEnumerable<int> SortByDistance(
        Cartesian2DTestHarness harness,
        PointCloudFixture fixture,
        NearQuery query)
    {
        return harness
            .QueryNear(query.Center, query.Radius)
            .Select(point => new
            {
                point.Id,
                Distance = EuclideanDistance(point.Position, query.Center)
            })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Id)
            .Select(item => item.Id);
    }
}
