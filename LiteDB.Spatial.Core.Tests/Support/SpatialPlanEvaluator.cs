using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Support;

public static class SpatialPlanEvaluator
{
    public static IReadOnlyList<SpatialCandidate> Encode(Cartesian2DEngine engine, IReadOnlyList<GeoPoint> points)
    {
        var mapper = engine.Mapper;
        var candidates = new List<SpatialCandidate>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var index = mapper.Encode(point);
            candidates.Add(new SpatialCandidate(i, point, index));
        }

        return candidates;
    }

    public static IReadOnlyList<SpatialCandidate> FilterByRanges(IReadOnlyList<SpatialIndexRange> ranges, IReadOnlyList<SpatialCandidate> candidates)
    {
        return candidates.Where(candidate => ranges.Any(range => candidate.Morton >= range.Start && candidate.Morton <= range.End)).ToList();
    }

    public static IReadOnlyList<SpatialCandidate> FilterByBounds(BoundingBox bounds, IReadOnlyList<SpatialCandidate> candidates)
    {
        return candidates.Where(candidate =>
        {
            var point = candidate.Point;
            return point.Longitude >= bounds.MinX && point.Longitude <= bounds.MaxX
                && point.Latitude >= bounds.MinY && point.Latitude <= bounds.MaxY;
        }).ToList();
    }
}

public sealed record SpatialCandidate(int Index, GeoPoint Point, ulong Morton);
