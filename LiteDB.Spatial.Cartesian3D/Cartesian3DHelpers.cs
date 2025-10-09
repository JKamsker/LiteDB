#nullable enable

using System;

namespace LiteDB.Spatial;

internal static class Cartesian3DHelpers
{
    internal static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinates must be finite numbers.", nameof(value));
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
    }

    internal static BoundingBox NormalizeBox3D(BoundingBox box)
    {
        return BoundingBox.From3D(
            Clamp01(box.MinX),
            Clamp01(box.MinY),
            Clamp01(box.MinZ),
            Clamp01(box.MaxX),
            Clamp01(box.MaxY),
            Clamp01(box.MaxZ));
    }
}
