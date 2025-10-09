using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes great-circle distances between geographic points using the Haversine formula.
/// </summary>
internal sealed class GeographicDistance : ISpatialDistance
{
    public double Distance(GeoPoint left, GeoPoint right)
    {
        return GeographicMath.HaversineDistance(left, right);
    }

    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Geographic distance is defined for two-dimensional coordinates only.");
    }
}
