using System;

namespace LiteDB.Spatial;

internal sealed class Cartesian3DDistance : ISpatialDistance
{
    public double Distance(GeoPoint left, GeoPoint right)
    {
        throw new NotSupportedException("Cartesian3D engine does not support 2D distances.");
    }

    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
