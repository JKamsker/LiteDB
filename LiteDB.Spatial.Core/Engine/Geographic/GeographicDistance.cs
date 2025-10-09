#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes great-circle distances between geographic points using the Haversine formula.
/// </summary>
public sealed class GeographicDistance : ISpatialDistance
{
    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        return GeographicMath.HaversineDistance(left, right);
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Geographic distance is defined only for two-dimensional points.");
    }
}
