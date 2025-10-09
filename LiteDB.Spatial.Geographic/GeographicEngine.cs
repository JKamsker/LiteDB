#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Spatial engine specialized for geographic (longitude/latitude) coordinates.
/// </summary>
public sealed class GeographicEngine : ISpatialEngine
{
    public const string EngineName = "Geographic2D";

    private readonly string _geometryFieldName;

    public GeographicEngine(
        SpatialIndexOptions? options = null,
        string geometryFieldName = SpatialCollectionDescriptor.DefaultGeometryFieldName)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;
        Mapper = new GeographicMapper(IndexEncoder, _geometryFieldName);
        Distance = new GeographicDistance();
    }

    public string Name => EngineName;

    public int Dimensions => 2;

    public SpatialIndexOptions Options { get; }

    public ISpatialIndexEncoder IndexEncoder { get; }

    public ISpatialMapper Mapper { get; }

    public ISpatialDistance Distance { get; }

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var effectiveRadius = radius + Options.DistanceTolerance;
        var boxes = GeographicMath.BuildCircleCovering(center, effectiveRadius);
        var covering = boxes.Count == 1 ? boxes[0] : ComputeCoveringBounds(boxes);
        return BuildPlan(boxes,
            covering,
            string.Format(CultureInfo.InvariantCulture, "distance <= {0} m (Haversine)", effectiveRadius));
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Geographic engine only supports two-dimensional near queries.");
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic engine requires a 2D bounding box.", nameof(bounds));
        }

        var boxes = GeographicMath.BuildBoundsCovering(bounds);
        var covering = boxes.Count == 1 ? boxes[0] : ComputeCoveringBounds(boxes);

        return BuildPlan(boxes, covering, "within geographic bounding box");
    }

    private ISpatialQueryPlan BuildPlan(IReadOnlyList<BoundingBox> boxes, BoundingBox covering, string predicate)
    {
        var ranges = new List<SpatialIndexRange>();

        foreach (var box in boxes)
        {
            var normalized = BoundingBox.From2D(
                GeographicMath.LongitudeToUnit(box.MinX),
                GeographicMath.LatitudeToUnit(box.MinY),
                GeographicMath.LongitudeToUnit(box.MaxX),
                GeographicMath.LatitudeToUnit(box.MaxY));

            var cover = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
            if (cover.Count > 0)
            {
                ranges.AddRange(cover);
            }
        }

        var merged = ranges.Count > 1
            ? MortonIndexEncoder.UnionAdjacentRanges(ranges)
            : ranges;

        return new SpatialQueryPlan(Dimensions, covering, merged, predicate);
    }

    private static BoundingBox ComputeCoveringBounds(IReadOnlyList<BoundingBox> boxes)
    {
        if (boxes.Count == 0)
        {
            throw new ArgumentException("At least one bounding box is required.", nameof(boxes));
        }

        var minLat = double.MaxValue;
        var maxLat = double.MinValue;

        foreach (var box in boxes)
        {
            minLat = Math.Min(minLat, box.MinY);
            maxLat = Math.Max(maxLat, box.MaxY);
        }

        return BoundingBox.From2D(-180d, minLat, 180d, maxLat);
    }
}
