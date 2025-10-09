#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Implements a WGS84-based spatial engine capable of producing index-aware query plans for geographic coordinates.
/// </summary>
public sealed class GeographicEngine : ISpatialEngine
{
    /// <summary>
    /// Gets the canonical name of the geographic engine.
    /// </summary>
    public const string EngineName = "Geographic2D";

    private readonly GeographicMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The document field that stores the geometry value.</param>
    /// <param name="options">Optional spatial index options; defaults are used when <c>null</c>.</param>
    public GeographicEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _mapper = new GeographicMapper(geometryFieldName, IndexEncoder);
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
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var effectiveRadius = radius + Options.DistanceTolerance;
        var boxes = GeographicBoundsBuilder.CircleToBoundingBoxes(center, effectiveRadius);
        var covering = GeographicBoundsBuilder.CombineForCovering(boxes);
        var ranges = GeographicBoundsBuilder.CoverWithEncoder(boxes, IndexEncoder, Options.MaxCoveringCells);
        var description = $"distance <= {radius:F3} m";

        return new SpatialQueryPlan(Dimensions, covering, ranges, description);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Geographic engine operates on two-dimensional coordinates.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic engine expects a two-dimensional bounding box.", nameof(bounds));
        }

        var boxes = GeographicBoundsBuilder.SplitBoundingBox(bounds);
        var covering = GeographicBoundsBuilder.CombineForCovering(boxes);
        var ranges = GeographicBoundsBuilder.CoverWithEncoder(boxes, IndexEncoder, Options.MaxCoveringCells);
        var description = "within bounding box";

        return new SpatialQueryPlan(Dimensions, covering, ranges, description);
    }
}
