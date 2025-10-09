using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

#nullable enable

/// <summary>
/// Spatial engine for two-dimensional Cartesian data.
/// </summary>
public sealed class Cartesian2DEngine : ISpatialEngine
{
    /// <summary>
    /// Canonical engine name stored in metadata.
    /// </summary>
    public const string EngineName = "Cartesian2D";

    private readonly Euclidean2DDistance _distance = new();

    public Cartesian2DEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(2, Options.PrecisionBits);
        Mapper = new Cartesian2DMapper(IndexEncoder, geometryFieldName);
    }

    public string Name => EngineName;

    public int Dimensions => 2;

    public SpatialIndexOptions Options { get; }

    public ISpatialIndexEncoder IndexEncoder { get; }

    public ISpatialMapper Mapper { get; }

    public ISpatialDistance Distance => _distance;

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        var expanded = Expand(radius);
        var minX = center.Longitude - expanded;
        var maxX = center.Longitude + expanded;
        var minY = center.Latitude - expanded;
        var maxY = center.Latitude + expanded;
        var bounds = BoundingBox.From2D(minX, minY, maxX, maxY);
        var normalized = Normalize(bounds);
        var ranges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        var description = string.Format(CultureInfo.InvariantCulture, "Euclidean distance <= {0:F6}", radius);
        return new SpatialQueryPlan(Dimensions, bounds, ranges, description);
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        throw new NotSupportedException("Cartesian2D engine does not support 3D near queries.");
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engine expects 2D bounding boxes.", nameof(bounds));
        }

        var normalized = Normalize(bounds);
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

    private BoundingBox Normalize(BoundingBox bounds)
    {
        var values = bounds.ToArray();
        values[0] = CartesianIndexing.Normalize(values[0]);
        values[1] = CartesianIndexing.Normalize(values[1]);
        values[2] = CartesianIndexing.Normalize(values[2]);
        values[3] = CartesianIndexing.Normalize(values[3]);
        return BoundingBox.Create(values);
    }

    private double Expand(double radius)
    {
        if (radius < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        }

        return radius * (1d + Options.DistanceTolerance);
    }
}
