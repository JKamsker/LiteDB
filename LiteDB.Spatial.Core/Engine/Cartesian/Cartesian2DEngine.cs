#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Spatial engine dedicated to two-dimensional Cartesian coordinates.
/// </summary>
public sealed class Cartesian2DEngine : ISpatialEngine
{
    /// <summary>
    /// The engine name persisted with metadata.
    /// </summary>
    public const string EngineName = "Cartesian2D";

    private readonly AxisExtent _xExtent;
    private readonly AxisExtent _yExtent;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian2DEngine"/> class.
    /// </summary>
    public Cartesian2DEngine(
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        AxisExtent? xExtent = null,
        AxisExtent? yExtent = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        _xExtent = xExtent ?? AxisExtent.Default;
        _yExtent = yExtent ?? AxisExtent.Default;

        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        Mapper = new CartesianMapper(geometryFieldName, IndexEncoder, _xExtent, _yExtent);
        Distance = new CartesianDistance();
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
        var minX = _xExtent.Clamp(center.Longitude - effectiveRadius);
        var minY = _yExtent.Clamp(center.Latitude - effectiveRadius);
        var maxX = _xExtent.Clamp(center.Longitude + effectiveRadius);
        var maxY = _yExtent.Clamp(center.Latitude + effectiveRadius);

        var ranges = CoverBounds(minX, minY, maxX, maxY);
        var coveringBounds = BoundingBox.From2D(
            _xExtent.Normalize(minX),
            _yExtent.Normalize(minY),
            _xExtent.Normalize(maxX),
            _yExtent.Normalize(maxY));

        var description = string.Format(CultureInfo.InvariantCulture, "Euclidean distance <= {0}", radius);
        return new SpatialQueryPlan(Dimensions, coveringBounds, ranges, description);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Cartesian2D engine does not support three-dimensional near queries.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine expects a two-dimensional bounding box.", nameof(bounds));
        }

        var minX = _xExtent.Clamp(bounds.MinX);
        var minY = _yExtent.Clamp(bounds.MinY);
        var maxX = _xExtent.Clamp(bounds.MaxX);
        var maxY = _yExtent.Clamp(bounds.MaxY);

        var ranges = CoverBounds(minX, minY, maxX, maxY);
        var coveringBounds = BoundingBox.From2D(
            _xExtent.Normalize(minX),
            _yExtent.Normalize(minY),
            _xExtent.Normalize(maxX),
            _yExtent.Normalize(maxY));

        return new SpatialQueryPlan(Dimensions, coveringBounds, ranges, "Within Cartesian bounding box");
    }

    /// <summary>
    /// Instantiates an engine from a stored descriptor.
    /// </summary>
    public static Cartesian2DEngine FromDescriptor(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!string.Equals(descriptor.EngineName, EngineName, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured with engine '{descriptor.EngineName}' which is incompatible with Cartesian2D facade operations.");
        }

        return new Cartesian2DEngine(descriptor.GeometryFieldName, descriptor.Options);
    }

    private IReadOnlyList<SpatialIndexRange> CoverBounds(double minX, double minY, double maxX, double maxY)
    {
        var normalized = BoundingBox.From2D(
            _xExtent.Normalize(minX),
            _yExtent.Normalize(minY),
            _xExtent.Normalize(maxX),
            _yExtent.Normalize(maxY));

        return IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
    }
}

