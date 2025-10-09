using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes Euclidean distances for three-dimensional Cartesian coordinates.
/// </summary>
public sealed class Euclidean3DDistance : ISpatialDistance
{
    public double Distance(GeoPoint left, GeoPoint right)
    {
        throw new NotSupportedException("Euclidean3DDistance expects 3D points.");
    }

    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
