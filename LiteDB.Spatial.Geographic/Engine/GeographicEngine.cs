using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LiteDB.Spatial;

#nullable enable

/// <summary>
/// Spatial engine that operates on geographic (longitude/latitude) coordinates.
/// </summary>
public sealed class GeographicEngine : ISpatialEngine
{
    /// <summary>
    /// Gets the canonical engine name persisted in metadata.
    /// </summary>
    public const string EngineName = "Geographic";

    private readonly GeographicDistance _distance = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The field that stores coordinates on documents.</param>
    /// <param name="options">Optional index configuration.</param>
    public GeographicEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        Mapper = new GeographicMapper(IndexEncoder, geometryFieldName);
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
    public ISpatialDistance Distance => _distance;

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        var expandedRadius = ExpandRadius(radius);
        var cover = GeographicIndexing.BuildNearCover(center, expandedRadius);
        var ranges = BuildRanges(cover.Segments);
        var description = string.Format(CultureInfo.InvariantCulture, "Haversine distance <= {0:F2} m", radius);
        return new SpatialQueryPlan(Dimensions, cover.CoveringBounds, ranges, description);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Geographic engine only handles two-dimensional near queries.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic engine expects 2D bounding boxes.", nameof(bounds));
        }

        var normalized = GeographicIndexing.NormalizeBoundingBox(bounds);
        var ranges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        var description = string.Format(
            CultureInfo.InvariantCulture,
            "Within bounding box [{0:F6}, {1:F6}, {2:F6}, {3:F6}]",
            bounds.MinX,
            bounds.MinY,
            bounds.MaxX,
            bounds.MaxY);

        return new SpatialQueryPlan(Dimensions, bounds, ranges, description);
    }

    private IReadOnlyList<SpatialIndexRange> BuildRanges(IReadOnlyList<BoundingBox> segments)
    {
        var allRanges = new List<SpatialIndexRange>();

        foreach (var segment in segments)
        {
            var normalized = GeographicIndexing.NormalizeBoundingBox(segment);
            var ranges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
            allRanges.AddRange(ranges);
        }

        return MortonIndexEncoder.UnionAdjacentRanges(allRanges);
    }

    private double ExpandRadius(double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        return radius * (1d + Options.DistanceTolerance);
    }
}
