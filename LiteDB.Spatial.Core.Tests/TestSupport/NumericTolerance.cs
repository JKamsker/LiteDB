using System;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class NumericTolerance
{
    private const double CartesianRelative = 1e-9;
    private const double CartesianAbsolute = 1e-9;

    public static double ForDistance(double scale)
    {
        var magnitude = Math.Abs(scale);
        return Math.Max(CartesianAbsolute, (CartesianRelative * magnitude) + CartesianAbsolute);
    }

    public static bool AlmostEquals(double expected, double actual, double scale)
    {
        return Math.Abs(expected - actual) <= ForDistance(scale);
    }
}
