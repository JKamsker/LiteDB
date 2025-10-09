using System;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers shared by Cartesian engines.
/// </summary>
internal static class CartesianIndexing
{
    public static double Normalize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinates must be finite numbers.", nameof(value));
        }

#if NET8_0_OR_GREATER
        return Math.Clamp(value, 0d, 1d);
#else
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
}
