#nullable enable

using System;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Plans queries for flat two-dimensional Cartesian coordinates.
/// </summary>
public sealed class Cartesian2DEngine : ISpatialEngine
{
    private readonly Cartesian2DMapper _mapper;
    private readonly EuclideanDistance _distance = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian2DEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The document field that stores the point geometry.</param>
    /// <param name="options">Optional spatial index options.</param>
    public Cartesian2DEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        _mapper = new Cartesian2DMapper(IndexEncoder, geometryFieldName);
    }

    /// <inheritdoc />
    public string Name => "Cartesian2D";

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

        var expansion = radius + Options.DistanceTolerance;
        var bounds = BoundingBox.From2D(
            center.Longitude - expansion,
            center.Latitude - expansion,
            center.Longitude + expansion,
            center.Latitude + expansion);

        return BuildPlan(bounds, string.Format(CultureInfo.InvariantCulture, "Euclidean2D distance ≤ {0:N3}", expansion));
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        return PlanNear(new GeoPoint(center.X, center.Y), radius);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian 2D queries require two-dimensional bounding boxes.", nameof(bounds));
        }

        return BuildPlan(bounds, "Within bounding box");
    }

    private SpatialQueryPlan BuildPlan(BoundingBox bounds, string predicate)
    {
        var cover = IndexEncoder.Cover(bounds, Options.MaxCoveringCells);
        var merged = MortonIndexEncoder.UnionAdjacentRanges(cover);
        return new SpatialQueryPlan(Dimensions, bounds, merged, predicate);
    }
}
