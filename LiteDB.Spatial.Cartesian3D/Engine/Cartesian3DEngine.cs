#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Provides spatial planning for point-based three-dimensional Cartesian coordinates.
/// </summary>
public sealed class Cartesian3DEngine : ICartesianSpatialEngine
{
    internal const string EngineNameValue = "Cartesian3D";
    public const string EngineName = EngineNameValue;

    private readonly BoundingBox _domain;
    private readonly CartesianNormalizer _normalizer;
    private readonly ISpatialIndexEncoder _encoder;
    private readonly Cartesian3DMapper _mapper;
    private readonly SpatialIndexOptions _options;

    public Cartesian3DEngine(string geometryFieldName, BoundingBox domain, SpatialIndexOptions options)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engine requires a 3D domain.", nameof(domain));
        }

        _options = options ?? throw new ArgumentNullException(nameof(options));
        _domain = domain;
        _normalizer = new CartesianNormalizer(domain);
        _encoder = new MortonIndexEncoder(3, options.PrecisionBits);
        _mapper = new Cartesian3DMapper(_normalizer, _encoder, geometryFieldName);
        Distance = new CartesianDistance(supports3D: true);
    }

    /// <inheritdoc />
    public string Name => EngineNameValue;

    /// <inheritdoc />
    public int Dimensions => 3;

    /// <inheritdoc />
    public SpatialIndexOptions Options => _options;

    /// <inheritdoc />
    public ISpatialIndexEncoder IndexEncoder => _encoder;

    /// <inheritdoc />
    public ISpatialMapper Mapper => _mapper;

    /// <inheritdoc />
    public ISpatialDistance Distance { get; }

    /// <inheritdoc />
    public BoundingBox Domain => _domain;

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        throw new NotSupportedException("Cartesian3D engine requires three-dimensional points for near queries.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        ValidateRadius(radius);
        ValidateFinite(center.X, nameof(center.X));
        ValidateFinite(center.Y, nameof(center.Y));
        ValidateFinite(center.Z, nameof(center.Z));

        var bounds = BoundingBox.From3D(
            center.X - radius,
            center.Y - radius,
            center.Z - radius,
            center.X + radius,
            center.Y + radius,
            center.Z + radius);

        return PlanFromBounds(bounds, string.Format(CultureInfo.InvariantCulture, "Euclidean3D <= {0:0.###}", radius));
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engine expects a 3D bounding box.", nameof(bounds));
        }

        return PlanFromBounds(bounds, "Within bounding box");
    }

    private SpatialQueryPlan PlanFromBounds(BoundingBox bounds, string predicate)
    {
        var normalized = _normalizer.Normalize(bounds);
        var ranges = _encoder.Cover(normalized, _options.MaxCoveringCells);
        var merged = MortonIndexEncoder.UnionAdjacentRanges(ranges);
        return new SpatialQueryPlan(Name, Dimensions, bounds, merged, predicate);
    }

    private static void ValidateRadius(double radius)
    {
        if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be a finite non-negative value.");
        }
    }

    private static void ValidateFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinate components must be finite numbers.", name);
        }
    }
}
