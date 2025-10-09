using System;

namespace LiteDB.Spatial;

internal sealed class Cartesian2DDistance : ISpatialDistance
{
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var dx = left.Longitude - right.Longitude;
        var dy = left.Latitude - right.Latitude;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Cartesian2D engine does not support 3D distances.");
    }
}
