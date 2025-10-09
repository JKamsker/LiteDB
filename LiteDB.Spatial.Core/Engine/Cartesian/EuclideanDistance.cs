#nullable enable

using System;

namespace LiteDB.Spatial;

internal sealed class EuclideanDistance : ISpatialDistance
{
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var dx = right.Longitude - left.Longitude;
        var dy = right.Latitude - left.Latitude;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        var dz = right.Z - left.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
