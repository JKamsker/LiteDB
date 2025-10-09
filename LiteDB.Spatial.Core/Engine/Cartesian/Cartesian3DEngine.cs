#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Spatial engine for three-dimensional Cartesian coordinates.
/// </summary>
public sealed class Cartesian3DEngine : ISpatialEngine
{
    /// <summary>
    /// Gets the canonical engine name.
    /// </summary>
    public const string EngineName = "Cartesian3D";

    private readonly Cartesian3DMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian3DEngine"/> class.
    /// </summary>
    public Cartesian3DEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(3, Options.PrecisionBits);
        _mapper = new Cartesian3DMapper(geometryFieldName, IndexEncoder);
        Mapper = _mapper;
        Distance = new EuclideanDistance();
    }

    /// <inheritdoc />
    public string Name => EngineName;

    /// <inheritdoc />
    public int Dimensions => 3;

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
        throw new NotSupportedException("Cartesian3D engine requires three-dimensional coordinates.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var effectiveRadius = radius + Options.DistanceTolerance;
        var minX = center.X - effectiveRadius;
        var maxX = center.X + effectiveRadius;
        var minY = center.Y - effectiveRadius;
        var maxY = center.Y + effectiveRadius;
        var minZ = center.Z - effectiveRadius;
        var maxZ = center.Z + effectiveRadius;

        var covering = BoundingBox.From3D(minX, minY, minZ, maxX, maxY, maxZ);
        var ranges = IndexEncoder.Cover(covering, Options.MaxCoveringCells);
        var description = $"distance <= {radius:F6}";

        return new SpatialQueryPlan(Dimensions, covering, ranges, description);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (!bounds.Is3D)
        {
            throw new ArgumentException("Cartesian3D engine expects a three-dimensional bounding box.", nameof(bounds));
        }

        var ranges = IndexEncoder.Cover(bounds, Options.MaxCoveringCells);
        return new SpatialQueryPlan(Dimensions, bounds, ranges, "within bounding box");
    }
}
