#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

public sealed class Cartesian2DEngine : ISpatialEngine
{
    public const string EngineName = "Cartesian2D";

    private readonly BoundingBox _coordinateSpace;
    private readonly double _minX;
    private readonly double _maxX;
    private readonly double _minY;
    private readonly double _maxY;
    private readonly double _rangeX;
    private readonly double _rangeY;

    public Cartesian2DEngine(
        SpatialIndexOptions? options = null,
        BoundingBox? coordinateSpace = null,
        string geometryFieldName = SpatialCollectionDescriptor.DefaultGeometryFieldName)
    {
        Options = options ?? new SpatialIndexOptions();
        _coordinateSpace = coordinateSpace ?? BoundingBox.From2D(0d, 0d, 1d, 1d);

        if (_coordinateSpace.Dimensions != 2)
        {
            throw new ArgumentException("Coordinate space must be a 2D bounding box.", nameof(coordinateSpace));
        }

        _minX = _coordinateSpace.MinX;
        _maxX = _coordinateSpace.MaxX;
        _minY = _coordinateSpace.MinY;
        _maxY = _coordinateSpace.MaxY;

        _rangeX = _maxX - _minX;
        _rangeY = _maxY - _minY;

        if (_rangeX <= 0d || _rangeY <= 0d)
        {
            throw new ArgumentException("Coordinate space must have positive extents on all axes.", nameof(coordinateSpace));
        }

        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        Mapper = new Cartesian2DMapper(IndexEncoder, _coordinateSpace, geometryFieldName);
        Distance = new Cartesian2DDistance();
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

        var minX = Math.Max(_minX, center.Longitude - effectiveRadius);
        var maxX = Math.Min(_maxX, center.Longitude + effectiveRadius);
        var minY = Math.Max(_minY, center.Latitude - effectiveRadius);
        var maxY = Math.Min(_maxY, center.Latitude + effectiveRadius);

        if (minX > maxX || minY > maxY)
        {
            return new SpatialQueryPlan(Dimensions, null, Array.Empty<SpatialIndexRange>(), "center outside coordinate space");
        }

        var covering = BoundingBox.From2D(minX, minY, maxX, maxY);
        var ranges = Cover(covering);
        var description = string.Format(CultureInfo.InvariantCulture, "euclidean distance <= {0}", effectiveRadius);
        return new SpatialQueryPlan(Dimensions, covering, ranges, description);
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Cartesian2D engine cannot plan three-dimensional near queries.");
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine expects a 2D bounding box.", nameof(bounds));
        }

        var minX = Math.Max(bounds.MinX, _minX);
        var maxX = Math.Min(bounds.MaxX, _maxX);
        var minY = Math.Max(bounds.MinY, _minY);
        var maxY = Math.Min(bounds.MaxY, _maxY);

        if (minX > maxX || minY > maxY)
        {
            return new SpatialQueryPlan(Dimensions, null, Array.Empty<SpatialIndexRange>(), "bounding box outside coordinate space");
        }

        var covering = BoundingBox.From2D(minX, minY, maxX, maxY);
        var ranges = Cover(covering);
        return new SpatialQueryPlan(Dimensions, covering, ranges, "within cartesian bounding box");
    }

    private IReadOnlyList<SpatialIndexRange> Cover(BoundingBox bounds)
    {
        var normalized = BoundingBox.From2D(
            Normalize(bounds.MinX, _minX, _rangeX),
            Normalize(bounds.MinY, _minY, _rangeY),
            Normalize(bounds.MaxX, _minX, _rangeX),
            Normalize(bounds.MaxY, _minY, _rangeY));

        var ranges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        return ranges.Count > 1 ? MortonIndexEncoder.UnionAdjacentRanges(ranges) : ranges;
    }

    private static double Normalize(double value, double min, double range)
    {
        var normalized = (value - min) / range;

        if (normalized < 0d)
        {
            return 0d;
        }

        if (normalized > 1d)
        {
            return 1d;
        }

        return normalized;
    }
}
