#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

/// <summary>
/// Provides geographic spatial planning against Morton encoded indexes.
/// </summary>
public sealed class GeographicEngine : ISpatialEngine
{
    /// <summary>
    /// The engine family name used when persisting metadata.
    /// </summary>
    public const string EngineFamilyName = "Geographic";

    private readonly GeographicDistanceMode _distanceMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicEngine"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The document field that contains the coordinate.</param>
    /// <param name="options">Index options.</param>
    /// <param name="distanceMode">Distance calculation mode.</param>
    public GeographicEngine(
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        Mapper = new GeographicMapper(geometryFieldName, IndexEncoder);
        Distance = new GeographicDistance(distanceMode);
        _distanceMode = distanceMode;
    }

    /// <inheritdoc />
    public string Name => GetEngineName(_distanceMode);

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
        var (minLon, minLat, maxLon, maxLat) = GeographicMath.BoundingBoxForCircle(center, effectiveRadius);

        var ranges = CoverGeographicBounds(minLon, minLat, maxLon, maxLat);
        var coveringBounds = CreateCoveringBounds(minLon, minLat, maxLon, maxLat, ranges.Count);
        var description = string.Format(CultureInfo.InvariantCulture, "Distance <= {0} m ({1})", radius, _distanceMode);

        return new SpatialQueryPlan(Dimensions, coveringBounds, ranges, description);
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Geographic engine does not support three-dimensional near queries.");
    }

    /// <inheritdoc />
    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic engine expects a two-dimensional bounding box.", nameof(bounds));
        }

        var minLat = GeographicMath.ClampLatitude(bounds.MinY);
        var maxLat = GeographicMath.ClampLatitude(bounds.MaxY);
        var minLon = bounds.MinX;
        var maxLon = bounds.MaxX;

        var ranges = CoverGeographicBounds(minLon, minLat, maxLon, maxLat);
        var coveringBounds = CreateCoveringBounds(minLon, minLat, maxLon, maxLat, ranges.Count);
        var description = "Within geographic bounding box";

        return new SpatialQueryPlan(Dimensions, coveringBounds, ranges, description);
    }

    /// <summary>
    /// Converts the stored engine name into the distance mode and constructs a concrete engine.
    /// </summary>
    public static GeographicEngine FromDescriptor(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!TryParseMode(descriptor.EngineName, out var mode))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured with engine '{descriptor.EngineName}' that is not compatible with the geographic engine facade.");
        }

        return new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options, mode);
    }

    /// <summary>
    /// Produces the persisted engine name for the supplied distance mode.
    /// </summary>
    public static string GetEngineName(GeographicDistanceMode mode)
    {
        return mode switch
        {
            GeographicDistanceMode.Haversine => EngineFamilyName + " (Haversine)",
            GeographicDistanceMode.Vincenty => EngineFamilyName + " (Vincenty)",
            _ => EngineFamilyName
        };
    }

    /// <summary>
    /// Attempts to parse the persisted engine name into a distance mode.
    /// </summary>
    public static bool TryParseMode(string engineName, out GeographicDistanceMode mode)
    {
        mode = GeographicDistanceMode.Haversine;

        if (string.IsNullOrWhiteSpace(engineName))
        {
            return false;
        }

        if (!engineName.StartsWith(EngineFamilyName, StringComparison.Ordinal))
        {
            return false;
        }

        if (engineName.Contains("Vincenty", StringComparison.OrdinalIgnoreCase))
        {
            mode = GeographicDistanceMode.Vincenty;
        }
        else
        {
            mode = GeographicDistanceMode.Haversine;
        }

        return true;
    }

    private IReadOnlyList<SpatialIndexRange> CoverGeographicBounds(double minLon, double minLat, double maxLon, double maxLat)
    {
        var latMin = GeographicMath.ClampLatitude(minLat);
        var latMax = GeographicMath.ClampLatitude(maxLat);
        var segments = GeographicMath.SplitLongitudeRange(minLon, maxLon);
        var ranges = new List<SpatialIndexRange>();

        foreach (var segment in segments)
        {
            var minLonUnit = GeographicMath.NormalizeLongitudeToUnit(segment.Min);
            var maxLonUnit = GeographicMath.NormalizeLongitudeToUnit(segment.Max);

            if (maxLonUnit < minLonUnit)
            {
                (minLonUnit, maxLonUnit) = (maxLonUnit, minLonUnit);
            }

            var normalizedBounds = BoundingBox.From2D(
                minLonUnit,
                GeographicMath.NormalizeLatitudeToUnit(latMin),
                maxLonUnit,
                GeographicMath.NormalizeLatitudeToUnit(latMax));

            var segmentRanges = IndexEncoder.Cover(normalizedBounds, Options.MaxCoveringCells);
            ranges.AddRange(segmentRanges);
        }

        return MortonIndexEncoder.UnionAdjacentRanges(ranges);
    }

    private BoundingBox? CreateCoveringBounds(double minLon, double minLat, double maxLon, double maxLat, int rangeCount)
    {
        if (rangeCount == 0)
        {
            return null;
        }

        var segments = GeographicMath.SplitLongitudeRange(minLon, maxLon);
        if (segments.Count > 1)
        {
            return null;
        }

        var segment = segments[0];
        var minLonUnit = GeographicMath.NormalizeLongitudeToUnit(segment.Min);
        var maxLonUnit = GeographicMath.NormalizeLongitudeToUnit(segment.Max);

        if (maxLonUnit < minLonUnit)
        {
            (minLonUnit, maxLonUnit) = (maxLonUnit, minLonUnit);
        }

        return BoundingBox.From2D(
            minLonUnit,
            GeographicMath.NormalizeLatitudeToUnit(minLat),
            maxLonUnit,
            GeographicMath.NormalizeLatitudeToUnit(maxLat));
    }
}

