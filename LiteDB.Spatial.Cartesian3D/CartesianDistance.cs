#nullable enable

using System;

namespace LiteDB.Spatial;

internal sealed class CartesianDistance : ISpatialDistance
{
    public double Distance(GeoPoint left, GeoPoint right)
    {
        var dx = left.Longitude - right.Longitude;
        var dy = left.Latitude - right.Latitude;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
