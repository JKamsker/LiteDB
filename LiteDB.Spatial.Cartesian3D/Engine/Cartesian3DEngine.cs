using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDB.Spatial;

#nullable enable

/// <summary>
/// Spatial engine for three-dimensional Cartesian point data.
/// </summary>
public sealed class Cartesian3DEngine : ISpatialEngine
{
    public const string EngineName = "Cartesian3D";

    private readonly Euclidean3DDistance _distance = new();

    public Cartesian3DEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        Options = options ?? new SpatialIndexOptions();
        IndexEncoder = new MortonIndexEncoder(3, Options.PrecisionBits);
        Mapper = new Cartesian3DMapper(IndexEncoder, geometryFieldName);
    }

    public string Name => EngineName;

    public int Dimensions => 3;

    public SpatialIndexOptions Options { get; }

    public ISpatialIndexEncoder IndexEncoder { get; }

    public ISpatialMapper Mapper { get; }

    public ISpatialDistance Distance => _distance;

    public ISpatialQueryPlan PlanNear(GeoPoint center, double radius)
    {
        throw new NotSupportedException("Cartesian3D engine expects three-dimensional points for near queries.");
    }

    public ISpatialQueryPlan PlanNear(GeoPoint3D center, double radius)
    {
        var expanded = Expand(radius);
        var bounds = BoundingBox.From3D(
            center.X - expanded,
            center.Y - expanded,
            center.Z - expanded,
            center.X + expanded,
            center.Y + expanded,
            center.Z + expanded);

        var normalized = Normalize(bounds);
        var ranges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        var description = string.Format(CultureInfo.InvariantCulture, "Euclidean3D distance <= {0:F6}", radius);
        return new SpatialQueryPlan(Dimensions, bounds, ranges, description);
    }

    public ISpatialQueryPlan PlanWithin(BoundingBox bounds)
    {
        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engine expects 3D bounding boxes.", nameof(bounds));
        }

        var normalized = Normalize(bounds);
        var ranges = IndexEncoder.Cover(normalized, Options.MaxCoveringCells);
        var description = string.Format(
            CultureInfo.InvariantCulture,
            "Within bounding box [{0:F6}, {1:F6}, {2:F6}, {3:F6}, {4:F6}, {5:F6}]",
            bounds.MinX,
            bounds.MinY,
            bounds.MinZ,
            bounds.MaxX,
            bounds.MaxY,
            bounds.MaxZ);
        return new SpatialQueryPlan(Dimensions, bounds, ranges, description);
    }

    private BoundingBox Normalize(BoundingBox bounds)
    {
        var values = bounds.ToArray();
        values[0] = Clamp(values[0]);
        values[1] = Clamp(values[1]);
        values[2] = Clamp(values[2]);
        values[3] = Clamp(values[3]);
        values[4] = Clamp(values[4]);
        values[5] = Clamp(values[5]);
        return BoundingBox.Create(values);
    }

    private static double Clamp(double value)
    {
#if NET8_0_OR_GREATER
        return Math.Clamp(value, 0d, 1d);
#else
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinates must be finite numbers.");
        }

        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
#endif
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
