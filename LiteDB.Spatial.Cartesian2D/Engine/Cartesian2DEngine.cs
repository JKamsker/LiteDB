#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Provides spatial planning for flat two-dimensional Cartesian coordinates.
/// </summary>
public sealed class Cartesian2DEngine : ICartesianSpatialEngine
{
    internal const string EngineNameValue = "Cartesian2D";
    public const string EngineName = EngineNameValue;

    private readonly BoundingBox _domain;
    private readonly CartesianNormalizer _normalizer;
    private readonly ISpatialIndexEncoder _encoder;
    private readonly Cartesian2DMapper _mapper;
    private readonly SpatialIndexOptions _options;

    public Cartesian2DEngine(string geometryFieldName, BoundingBox domain, SpatialIndexOptions options)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        if (domain.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine requires a 2D domain.", nameof(domain));
        }

        _options = options ?? throw new ArgumentNullException(nameof(options));
        _domain = domain;
        _normalizer = new CartesianNormalizer(domain);
        _encoder = new MortonIndexEncoder(2, options.PrecisionBits);
        _mapper = new Cartesian2DMapper(_normalizer, _encoder, geometryFieldName);
        Distance = new CartesianDistance(supports3D: false);
    }

    /// <inheritdoc />
    public string Name => EngineNameValue;

    /// <inheritdoc />
    public int Dimensions => 2;

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
        ValidateRadius(radius);
        ValidateFinite(center.Longitude, nameof(center.Longitude));
        ValidateFinite(center.Latitude, nameof(center.Latitude));

        var minX = center.Longitude - radius;
        var minY = center.Latitude - radius;
        var maxX = center.Longitude + radius;
        var maxY = center.Latitude + radius;
        var bounds = BoundingBox.From2D(minX, minY, maxX, maxY);
        return PlanFromBounds(bounds, string.Format(CultureInfo.InvariantCulture, "Euclidean <= {0:0.###}", radius));
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Cartesian2D engine does not support 3D geometry.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine expects a 2D bounding box.", nameof(bounds));
        }

        return PlanFromBounds(bounds, "Within bounding box");
    }

    private SpatialQueryPlan PlanFromBounds(BoundingBox bounds, string predicate)
    {
        var normalized = _normalizer.Normalize(bounds);
        var ranges = _encoder.Cover(normalized, _options.MaxCoveringCells);
        var merged = MortonIndexEncoder.UnionAdjacentRanges(ranges);
        return new SpatialQueryPlan(Dimensions, bounds, merged, predicate);
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
