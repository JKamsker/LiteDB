#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Provides query planning and mapping for two-dimensional geographic data stored in LiteDB.
/// </summary>
public sealed class GeographicEngine : ISpatialEngine
{
    private readonly GeographicMapper _mapper;
    private readonly GeographicDistance _distance;
    private readonly GeographicDistanceMode _mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The document field that stores the geographic point.</param>
    /// <param name="options">Optional spatial index options. Defaults are used when <c>null</c>.</param>
    /// <param name="mode">The distance mode used for exact filtering.</param>
    public GeographicEngine(string geometryFieldName, SpatialIndexOptions? options = null, GeographicDistanceMode mode = GeographicDistanceMode.Vincenty)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        _mode = mode;
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _mapper = new GeographicMapper(IndexEncoder, geometryFieldName);
        _distance = new GeographicDistance(mode);
    }

    /// <inheritdoc />
    public string Name => "Geographic";

    /// <inheritdoc />
    public int Dimensions => 2;

    /// <inheritdoc />
    public SpatialIndexOptions Options { get; }

    /// <inheritdoc />
    public ISpatialIndexEncoder IndexEncoder { get; }

    /// <inheritdoc />
    public ISpatialMapper Mapper => _mapper;

    /// <inheritdoc />
    public ISpatialDistance Distance => _distance;

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var envelopes = GeographicMath.CreateBoundingBoxesForCircle(center, radius + Options.DistanceTolerance);
        return BuildPlan(envelopes, string.Format(CultureInfo.InvariantCulture, "{0} distance ≤ {1:N3} m", _mode, radius + Options.DistanceTolerance));
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        return PlanNear(new GeoPoint(center.X, center.Y), radius);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        var normalized = GeographicMath.NormalizeBoundingBox(bounds);
        return BuildPlan(normalized, "Within bounding box");
    }

    private SpatialQueryPlan BuildPlan(IReadOnlyList<BoundingBox> segments, string predicate)
    {
        var ranges = new List<SpatialIndexRange>();
        BoundingBox? covering = null;

        foreach (var segment in segments)
        {
            var normalized = GeographicMath.ToNormalizedBox(segment);
            covering ??= normalized;
            var coverRanges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
            if (coverRanges.Count > 0)
            {
                ranges.AddRange(coverRanges);
            }
        }

        var merged = MortonIndexEncoder.UnionAdjacentRanges(ranges);
        return new SpatialQueryPlan(Dimensions, covering, merged, predicate);
    }
}
