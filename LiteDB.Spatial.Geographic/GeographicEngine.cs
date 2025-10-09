#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Geographic (WGS84) spatial engine providing index-aware planners for point data.
/// </summary>
public sealed class GeographicEngine : ISpatialEngine
{
    /// <summary>
    /// The registered engine name.
    /// </summary>
    public const string EngineName = "Geographic";

    private readonly GeographicMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicEngine"/> class.
    /// </summary>
    /// <param name="geometryField">Name of the field that stores the geometry payload.</param>
    /// <param name="options">Optional index options overriding the defaults.</param>
    public GeographicEngine(string geometryField, SpatialIndexOptions? options = null)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _mapper = new GeographicMapper(string.IsNullOrWhiteSpace(geometryField)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryField, IndexEncoder);
        Mapper = _mapper;
        Distance = new GeographicDistance();
    }

    /// <inheritdoc />
    public string Name => EngineName;

    /// <inheritdoc />
    public int Dimensions => 2;

    /// <inheritdoc />
    public SpatialIndexOptions Options { get; }

    /// <inheritdoc />
    public ISpatialIndexEncoder IndexEncoder { get; }

    /// <inheritdoc />
    public ISpatialMapper Mapper { get; }

    /// <inheritdoc />
    public ISpatialDistance Distance { get; }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be a non-negative finite number.");
        }

        var expandedRadius = radius + Options.DistanceTolerance;
        var bounds = GeographicHelpers.CreateBounds(center, expandedRadius);
        var ranges = BuildIndexRanges(bounds);

        var predicate = string.Format(
            CultureInfo.InvariantCulture,
            "distance<= {0:F3}m around ({1:F6},{2:F6})",
            radius,
            center.Longitude,
            center.Latitude);

        return new SpatialQueryPlan(Dimensions, bounds.ToBoundingBox(), ranges, predicate);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Geographic engine is limited to two-dimensional coordinates.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        var geographicBounds = GeographicHelpers.FromBoundingBox(bounds);
        var ranges = BuildIndexRanges(geographicBounds);

        var predicate = string.Format(
            CultureInfo.InvariantCulture,
            "within [{0:F6},{1:F6}]..[{2:F6},{3:F6}]",
            geographicBounds.MinLongitude,
            geographicBounds.MinLatitude,
            geographicBounds.MaxLongitude,
            geographicBounds.MaxLatitude);

        return new SpatialQueryPlan(Dimensions, geographicBounds.ToBoundingBox(), ranges, predicate);
    }

    private IReadOnlyList<SpatialIndexRange> BuildIndexRanges(GeographicBounds bounds)
    {
        var segments = bounds.SplitForCover();
        var ranges = new List<SpatialIndexRange>();

        foreach (var segment in segments)
        {
            var normalized = segment.ToNormalizedBoundingBox();
            var cover = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
            ranges.AddRange(cover);
        }

        return MortonIndexEncoder.UnionAdjacentRanges(ranges);
    }
}
