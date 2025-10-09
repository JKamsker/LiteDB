#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Spatial engine for two-dimensional Cartesian coordinates using Euclidean distance.
/// </summary>
public sealed class Cartesian2DEngine : ISpatialEngine
{
    /// <summary>
    /// Gets the canonical engine name.
    /// </summary>
    public const string EngineName = "Cartesian2D";

    private readonly Cartesian2DMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian2DEngine"/> class.
    /// </summary>
    public Cartesian2DEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _mapper = new Cartesian2DMapper(geometryFieldName, IndexEncoder);
        Mapper = _mapper;
        Distance = new EuclideanDistance();
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
        var minX = center.Longitude - effectiveRadius;
        var maxX = center.Longitude + effectiveRadius;
        var minY = center.Latitude - effectiveRadius;
        var maxY = center.Latitude + effectiveRadius;

        var covering = BoundingBox.From2D(minX, minY, maxX, maxY);
        var ranges = IndexEncoder.Cover(covering, Options.MaxCoveringCells);
        var description = $"distance <= {radius:F6}";

        return new SpatialQueryPlan(Dimensions, covering, ranges, description);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Cartesian2D engine operates on two-dimensional coordinates.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine expects a two-dimensional bounding box.", nameof(bounds));
        }

        var ranges = IndexEncoder.Cover(bounds, Options.MaxCoveringCells);
        return new SpatialQueryPlan(Dimensions, bounds, ranges, "within bounding box");
    }
}
