using System;

namespace LiteDB.Spatial.Core.Tests.Support;

public static class NumericTolerance
{
    private const double EuclideanSlope = 1e-9;
    private const double EuclideanIntercept = 1e-9;

    public static double Euclidean(double scale)
    {
        var magnitude = Math.Abs(scale);
        return (magnitude * EuclideanSlope) + EuclideanIntercept;
    }

    public static bool WithinEuclidean(double expected, double actual, double scale)
    {
        var tolerance = Euclidean(scale);
        return Math.Abs(expected - actual) <= tolerance;
    }
}
