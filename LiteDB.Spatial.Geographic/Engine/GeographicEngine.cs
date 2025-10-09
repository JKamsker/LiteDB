#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Provides query planning and document mapping for two-dimensional geographic coordinates.
/// </summary>
public sealed class GeographicEngine : IGeographicSpatialEngine
{
    internal const string EngineNameValue = "Geographic2D";
    public const string EngineName = EngineNameValue;

    private readonly GeographicDistanceMode _distanceMode;
    private readonly GeographicMapper _mapper;
    private readonly ISpatialIndexEncoder _encoder;
    private readonly SpatialIndexOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The field containing the geographic point document.</param>
    /// <param name="options">The index options used during mapping and planning.</param>
    /// <param name="distanceMode">The distance algorithm applied during exact filtering.</param>
    public GeographicEngine(string geometryFieldName, SpatialIndexOptions options, GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        _options = options ?? throw new ArgumentNullException(nameof(options));
        _distanceMode = distanceMode;
        _encoder = new MortonIndexEncoder(2, options.PrecisionBits);
        _mapper = new GeographicMapper(_encoder, geometryFieldName);
        Distance = new GeographicDistance(distanceMode);
    }

    /// <inheritdoc />
    public string Name => EngineNameValue;

    /// <inheritdoc />
    public int Dimensions => 2;

    /// <summary>
    /// Gets the distance mode applied by the engine when evaluating exact predicates.
    /// </summary>
    public GeographicDistanceMode DistanceMode => _distanceMode;

    /// <inheritdoc />
    public SpatialIndexOptions Options => _options;

    /// <inheritdoc />
    public ISpatialIndexEncoder IndexEncoder => _encoder;

    /// <inheritdoc />
    public ISpatialMapper Mapper => _mapper;

    /// <inheritdoc />
    public ISpatialDistance Distance { get; }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        ValidateGeoPoint(center);
        ValidateRadius(radius);

        var effectiveRadius = radius + _options.DistanceTolerance;
        var segments = GeographicBoundsBuilder.FromCircle(center, effectiveRadius);
        return BuildPlanFromSegments(segments, radius, "meters");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Geographic engine only supports two-dimensional points.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic engine expects a two-dimensional bounding box.", nameof(bounds));
        }

        var segments = GeographicBoundsBuilder.FromBoundingBox(bounds);
        return BuildPlanFromSegments(segments, 0d, "within");
    }

    private SpatialQueryPlan BuildPlanFromSegments(IReadOnlyList<GeographicBoundsSegment> segments, double radius, string predicateUnit)
    {
        var ranges = new List<SpatialIndexRange>();
        ulong cellCountEstimate = 0;
        var clipped = false;

        foreach (var segment in segments)
        {
            var covering = _encoder.Cover(segment.Normalized, _options.MaxCoveringCells);
            ranges.AddRange(covering.Ranges);
            cellCountEstimate = SaturatingAdd(cellCountEstimate, covering.CellCountEstimate);
            clipped |= covering.WasClippedByMaxCells;
        }

        var mergedRanges = MortonIndexEncoder.UnionAdjacentRanges(ranges);
        var metrics = mergedRanges.Count == 0 && segments.Count == 0
            ? SpatialCoveringMetrics.Empty
            : new SpatialCoveringMetrics(
                cellCountEstimate,
                _options.MaxCoveringCells,
                mergedRanges.Count,
                clipped);
        var predicate = predicateUnit == "within"
            ? "Within bounding box"
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} distance <= {1:0.##} {2}", _distanceMode, radius, predicateUnit);

        BoundingBox? coveringBounds = segments.Count switch
        {
            0 => (BoundingBox?)null,
            1 => segments[0].World,
            _ => BoundingBox.From2D(-180d, segments.Min(s => s.MinLatitude), 180d, segments.Max(s => s.MaxLatitude))
        };

        return new SpatialQueryPlan(Name, Dimensions, coveringBounds, mergedRanges, predicate, metrics);
    }

    private static ulong SaturatingAdd(ulong left, ulong right)
    {
        var remaining = ulong.MaxValue - left;
        return right > remaining ? ulong.MaxValue : left + right;
    }

    private static void ValidateGeoPoint(GeoPoint point)
    {
        if (point.Longitude is < -180d or > 180d)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Longitude must be within [-180, 180] degrees.");
        }

        if (point.Latitude is < -90d or > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Latitude must be within [-90, 90] degrees.");
        }
    }

    private static void ValidateRadius(double radius)
    {
        if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be a finite non-negative number.");
        }
    }
}
