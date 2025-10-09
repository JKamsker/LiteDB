#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

public sealed class Cartesian3DEngine : ISpatialEngine
{
    public const string EngineName = "Cartesian3D";

    private readonly BoundingBox _coordinateSpace;
    private readonly double _minX;
    private readonly double _maxX;
    private readonly double _minY;
    private readonly double _maxY;
    private readonly double _minZ;
    private readonly double _maxZ;
    private readonly double _rangeX;
    private readonly double _rangeY;
    private readonly double _rangeZ;

    public Cartesian3DEngine(
        SpatialIndexOptions? options = null,
        BoundingBox? coordinateSpace = null,
        string geometryFieldName = SpatialCollectionDescriptor.DefaultGeometryFieldName)
    {
        Options = options ?? new SpatialIndexOptions();
        _coordinateSpace = coordinateSpace ?? BoundingBox.From3D(0d, 0d, 0d, 1d, 1d, 1d);

        if (_coordinateSpace.Dimensions != 3)
        {
            throw new ArgumentException("Coordinate space must be a 3D bounding box.", nameof(coordinateSpace));
        }

        _minX = _coordinateSpace.MinX;
        _maxX = _coordinateSpace.MaxX;
        _minY = _coordinateSpace.MinY;
        _maxY = _coordinateSpace.MaxY;
        _minZ = _coordinateSpace.MinZ;
        _maxZ = _coordinateSpace.MaxZ;

        _rangeX = _maxX - _minX;
        _rangeY = _maxY - _minY;
        _rangeZ = _maxZ - _minZ;

        if (_rangeX <= 0d || _rangeY <= 0d || _rangeZ <= 0d)
        {
            throw new ArgumentException("Coordinate space must have positive extents on all axes.", nameof(coordinateSpace));
        }

        IndexEncoder = new MortonIndexEncoder(3, Options.PrecisionBits);
        Mapper = new Cartesian3DMapper(IndexEncoder, _coordinateSpace, geometryFieldName);
        Distance = new Cartesian3DDistance();
    }

    public string Name => EngineName;

    public int Dimensions => 3;

    public SpatialIndexOptions Options { get; }

    public ISpatialIndexEncoder IndexEncoder { get; }

    public ISpatialMapper Mapper { get; }

    public ISpatialDistance Distance { get; }

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        throw new NotSupportedException("Cartesian3D engine cannot plan two-dimensional near queries.");
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var effectiveRadius = radius + Options.DistanceTolerance;

        var minX = Math.Max(_minX, center.X - effectiveRadius);
        var maxX = Math.Min(_maxX, center.X + effectiveRadius);
        var minY = Math.Max(_minY, center.Y - effectiveRadius);
        var maxY = Math.Min(_maxY, center.Y + effectiveRadius);
        var minZ = Math.Max(_minZ, center.Z - effectiveRadius);
        var maxZ = Math.Min(_maxZ, center.Z + effectiveRadius);

        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            return new SpatialQueryPlan(Dimensions, null, Array.Empty<SpatialIndexRange>(), "center outside coordinate space");
        }

        var covering = BoundingBox.From3D(minX, minY, minZ, maxX, maxY, maxZ);
        var ranges = Cover(covering);
        var description = string.Format(CultureInfo.InvariantCulture, "euclidean3d distance <= {0}", effectiveRadius);
        return new SpatialQueryPlan(Dimensions, covering, ranges, description);
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engine expects a 3D bounding box.", nameof(bounds));
        }

        var minX = Math.Max(bounds.MinX, _minX);
        var maxX = Math.Min(bounds.MaxX, _maxX);
        var minY = Math.Max(bounds.MinY, _minY);
        var maxY = Math.Min(bounds.MaxY, _maxY);
        var minZ = Math.Max(bounds.MinZ, _minZ);
        var maxZ = Math.Min(bounds.MaxZ, _maxZ);

        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            return new SpatialQueryPlan(Dimensions, null, Array.Empty<SpatialIndexRange>(), "bounding box outside coordinate space");
        }

        var covering = BoundingBox.From3D(minX, minY, minZ, maxX, maxY, maxZ);
        var ranges = Cover(covering);
        return new SpatialQueryPlan(Dimensions, covering, ranges, "within cartesian 3d bounding box");
    }

    private IReadOnlyList<SpatialIndexRange> Cover(BoundingBox bounds)
    {
        var normalized = BoundingBox.From3D(
            Normalize(bounds.MinX, _minX, _rangeX),
            Normalize(bounds.MinY, _minY, _rangeY),
            Normalize(bounds.MinZ, _minZ, _rangeZ),
            Normalize(bounds.MaxX, _minX, _rangeX),
            Normalize(bounds.MaxY, _minY, _rangeY),
            Normalize(bounds.MaxZ, _minZ, _rangeZ));

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
