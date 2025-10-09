#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Spatial engine for three-dimensional Cartesian coordinates.
/// </summary>
public sealed class Cartesian3DEngine : ISpatialEngine
{
    /// <summary>
    /// The engine name persisted with metadata.
    /// </summary>
    public const string EngineName = "Cartesian3D";

    private readonly AxisExtent _xExtent;
    private readonly AxisExtent _yExtent;
    private readonly AxisExtent _zExtent;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian3DEngine"/> class.
    /// </summary>
    public Cartesian3DEngine(
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        AxisExtent? xExtent = null,
        AxisExtent? yExtent = null,
        AxisExtent? zExtent = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        _xExtent = xExtent ?? AxisExtent.Default;
        _yExtent = yExtent ?? AxisExtent.Default;
        _zExtent = zExtent ?? AxisExtent.Default;

        IndexEncoder = new MortonIndexEncoder(3, Options.PrecisionBits);
        Mapper = new CartesianMapper(geometryFieldName, IndexEncoder, _xExtent, _yExtent, _zExtent);
        Distance = new CartesianDistance();
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
        var minX = _xExtent.Clamp(center.X - effectiveRadius);
        var minY = _yExtent.Clamp(center.Y - effectiveRadius);
        var minZ = _zExtent.Clamp(center.Z - effectiveRadius);
        var maxX = _xExtent.Clamp(center.X + effectiveRadius);
        var maxY = _yExtent.Clamp(center.Y + effectiveRadius);
        var maxZ = _zExtent.Clamp(center.Z + effectiveRadius);

        var ranges = CoverBounds(minX, minY, minZ, maxX, maxY, maxZ);
        var coveringBounds = BoundingBox.From3D(
            _xExtent.Normalize(minX),
            _yExtent.Normalize(minY),
            _zExtent.Normalize(minZ),
            _xExtent.Normalize(maxX),
            _yExtent.Normalize(maxY),
            _zExtent.Normalize(maxZ));

        return new SpatialQueryPlan(Dimensions, coveringBounds, ranges, "Within Cartesian radius");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engine expects a three-dimensional bounding box.", nameof(bounds));
        }

        var minX = _xExtent.Clamp(bounds.MinX);
        var minY = _yExtent.Clamp(bounds.MinY);
        var minZ = _zExtent.Clamp(bounds.MinZ);
        var maxX = _xExtent.Clamp(bounds.MaxX);
        var maxY = _yExtent.Clamp(bounds.MaxY);
        var maxZ = _zExtent.Clamp(bounds.MaxZ);

        var ranges = CoverBounds(minX, minY, minZ, maxX, maxY, maxZ);
        var coveringBounds = BoundingBox.From3D(
            _xExtent.Normalize(minX),
            _yExtent.Normalize(minY),
            _zExtent.Normalize(minZ),
            _xExtent.Normalize(maxX),
            _yExtent.Normalize(maxY),
            _zExtent.Normalize(maxZ));

        return new SpatialQueryPlan(Dimensions, coveringBounds, ranges, "Within 3D Cartesian bounding box");
    }

    /// <summary>
    /// Instantiates an engine from persisted metadata.
    /// </summary>
    public static Cartesian3DEngine FromDescriptor(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!string.Equals(descriptor.EngineName, EngineName, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured with engine '{descriptor.EngineName}' which is incompatible with Cartesian3D facade operations.");
        }

        return new Cartesian3DEngine(descriptor.GeometryFieldName, descriptor.Options);
    }

    private IReadOnlyList<SpatialIndexRange> CoverBounds(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        var normalized = BoundingBox.From3D(
            _xExtent.Normalize(minX),
            _yExtent.Normalize(minY),
            _zExtent.Normalize(minZ),
            _xExtent.Normalize(maxX),
            _yExtent.Normalize(maxY),
            _zExtent.Normalize(maxZ));

        return IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
    }
}

