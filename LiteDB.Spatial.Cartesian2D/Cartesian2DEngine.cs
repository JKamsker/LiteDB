#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

public sealed class Cartesian2DEngine : ISpatialEngine
{
    public const string EngineName = "Cartesian2D";

    private readonly Cartesian2DMapper _mapper;
    private readonly CartesianDistance _distance = new();

    public Cartesian2DEngine(string geometryField, SpatialIndexOptions? options = null)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _mapper = new Cartesian2DMapper(string.IsNullOrWhiteSpace(geometryField)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryField, IndexEncoder);
        Mapper = _mapper;
    }

    public string Name => EngineName;

    public int Dimensions => 2;

    public SpatialIndexOptions Options { get; }

    public ISpatialIndexEncoder IndexEncoder { get; }

    public ISpatialMapper Mapper { get; }

    public ISpatialDistance Distance => _distance;

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be a non-negative finite number.");
        }

        var expanded = radius + Options.DistanceTolerance;
        var bounds = BoundingBox.From2D(
            center.Longitude - expanded,
            center.Latitude - expanded,
            center.Longitude + expanded,
            center.Latitude + expanded);

        var ranges = BuildIndexRanges(bounds);
        var predicate = string.Format(
            CultureInfo.InvariantCulture,
            "distance<= {0:F3} around ({1:F6},{2:F6})",
            radius,
            center.Longitude,
            center.Latitude);

        return new SpatialQueryPlan(Dimensions, bounds, ranges, predicate);
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Cartesian2D engine cannot plan three-dimensional radius queries.");
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine requires two-dimensional bounds.", nameof(bounds));
        }

        var ranges = BuildIndexRanges(bounds);
        var predicate = string.Format(
            CultureInfo.InvariantCulture,
            "within [{0:F6},{1:F6}]..[{2:F6},{3:F6}]",
            bounds.MinX,
            bounds.MinY,
            bounds.MaxX,
            bounds.MaxY);

        return new SpatialQueryPlan(Dimensions, bounds, ranges, predicate);
    }

    private IReadOnlyList<SpatialIndexRange> BuildIndexRanges(BoundingBox bounds)
    {
        var normalized = CartesianHelpers.NormalizeBox2D(bounds);
        var cover = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        return MortonIndexEncoder.UnionAdjacentRanges(cover);
    }
}
