#nullable enable

using System;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Provides spatial planning for three-dimensional Cartesian point clouds.
/// </summary>
public sealed class Cartesian3DEngine : ISpatialEngine
{
    private readonly Cartesian3DMapper _mapper;
    private readonly EuclideanDistance _distance = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian3DEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The document field containing the 3D point.</param>
    /// <param name="options">Optional spatial index options.</param>
    public Cartesian3DEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(3, Options.PrecisionBits);
        _mapper = new Cartesian3DMapper(IndexEncoder, geometryFieldName);
    }

    /// <inheritdoc />
    public string Name => "Cartesian3D";

    /// <inheritdoc />
    public int Dimensions => 3;

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
        return PlanNear(new GeoPoint3D(center.Longitude, center.Latitude, 0d), radius);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        var expansion = radius + Options.DistanceTolerance;
        var bounds = BoundingBox.From3D(
            center.X - expansion,
            center.Y - expansion,
            center.Z - expansion,
            center.X + expansion,
            center.Y + expansion,
            center.Z + expansion);

        return BuildPlan(bounds, string.Format(CultureInfo.InvariantCulture, "Euclidean3D distance ≤ {0:N3}", expansion));
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian 3D queries require three-dimensional bounding boxes.", nameof(bounds));
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
