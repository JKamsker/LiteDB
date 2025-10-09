extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class SpatialPlanInspector
{
    public static NearCandidateMetrics InspectNear(Cartesian2DTestHarness harness, GeoPoint center, double radius)
    {
        if (harness == null)
        {
            throw new ArgumentNullException(nameof(harness));
        }

        var plan = harness.PlanNear(center, radius);
        var indexExpression = BuildIndexExpression(harness.Descriptor, plan.IndexRanges);
        var indexCandidates = harness.QueryByExpression(indexExpression);

        var coveringCandidates = plan.CoveringBounds.HasValue
            ? indexCandidates.Where(point => Contains(plan.CoveringBounds.Value, point.Position)).ToList()
            : indexCandidates.ToList();

        var exactMatches = harness.QueryNear(center, radius);

        return new NearCandidateMetrics(
            indexCandidates.Count,
            coveringCandidates.Count,
            exactMatches.Count,
            exactMatches.Select(point => new NearResult(point.Id, point.Position)).ToList());
    }

    private static BaseLiteDB.BsonExpression? BuildIndexExpression(SpatialCollectionDescriptor descriptor, IReadOnlyList<SpatialIndexRange> ranges)
    {
        if (ranges == null)
        {
            throw new ArgumentNullException(nameof(ranges));
        }

        if (ranges.Count == 0)
        {
            return null;
        }

        var expressions = new List<BaseLiteDB.BsonExpression>(ranges.Count);
        var field = $"$.{descriptor.Options.IndexFieldName}";

        foreach (var range in ranges)
        {
            var start = CreateIndexValue(range.Start);
            var end = CreateIndexValue(range.End);
            expressions.Add(BaseLiteDB.Query.Between(field, start, end));
        }

        return expressions.Count == 1 ? expressions[0] : BaseLiteDB.Query.Or(expressions.ToArray());
    }

    private static BaseLiteDB.BsonValue CreateIndexValue(ulong value)
    {
        if (value <= long.MaxValue)
        {
            return new BaseLiteDB.BsonValue((long)value);
        }

        return new BaseLiteDB.BsonValue((decimal)value);
    }

    private static bool Contains(BoundingBox bounds, GeoPoint point)
    {
        var values = bounds.GetValues();
        var minX = values[0];
        var minY = values[1];
        var maxX = values[2];
        var maxY = values[3];

        return point.Longitude >= minX && point.Longitude <= maxX && point.Latitude >= minY && point.Latitude <= maxY;
    }

    internal sealed record NearCandidateMetrics(
        int IndexCandidates,
        int CoveringCandidates,
        int ExactMatches,
        IReadOnlyList<NearResult> Results);

    internal sealed record NearResult(int Id, GeoPoint Position);
}
