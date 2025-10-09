#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

public sealed class Cartesian3DEngine : ISpatialEngine
{
    public const string EngineName = "Cartesian3D";

    private readonly Cartesian3DMapper _mapper;
    private readonly CartesianDistance _distance = new();

    public Cartesian3DEngine(string geometryField, SpatialIndexOptions? options = null)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(3, Options.PrecisionBits);
        _mapper = new Cartesian3DMapper(string.IsNullOrWhiteSpace(geometryField)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryField, IndexEncoder);
        Mapper = _mapper;
    }

    public string Name => EngineName;

    public int Dimensions => 3;

    public SpatialIndexOptions Options { get; }

    public ISpatialIndexEncoder IndexEncoder { get; }

    public ISpatialMapper Mapper { get; }

    public ISpatialDistance Distance => _distance;

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        throw new NotSupportedException("Cartesian3D engine requires three-dimensional coordinates for near queries.");
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be a non-negative finite number.");
        }

        var expanded = radius + Options.DistanceTolerance;
        var bounds = BoundingBox.From3D(
            center.X - expanded,
            center.Y - expanded,
            center.Z - expanded,
            center.X + expanded,
            center.Y + expanded,
            center.Z + expanded);

        var ranges = BuildIndexRanges(bounds);
        var predicate = string.Format(
            CultureInfo.InvariantCulture,
            "distance<= {0:F3} around ({1:F6},{2:F6},{3:F6})",
            radius,
            center.X,
            center.Y,
            center.Z);

        return new SpatialQueryPlan(Dimensions, bounds, ranges, predicate);
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engine requires three-dimensional bounds.", nameof(bounds));
        }

        var ranges = BuildIndexRanges(bounds);
        var predicate = string.Format(
            CultureInfo.InvariantCulture,
            "within [{0:F6},{1:F6},{2:F6}]..[{3:F6},{4:F6},{5:F6}]",
            bounds.MinX,
            bounds.MinY,
            bounds.MinZ,
            bounds.MaxX,
            bounds.MaxY,
            bounds.MaxZ);

        return new SpatialQueryPlan(Dimensions, bounds, ranges, predicate);
    }

    private IReadOnlyList<SpatialIndexRange> BuildIndexRanges(BoundingBox bounds)
    {
        var normalized = Cartesian3DHelpers.NormalizeBox3D(bounds);
        var cover = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        return MortonIndexEncoder.UnionAdjacentRanges(cover);
    }
}
