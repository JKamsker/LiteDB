using System;
using LiteDB.Spatial;
using MathNet.Numerics;

namespace LiteDB.Spatial.Core.Tests.Differential;

internal static class MathNetOracle3D
{
    public static double Distance(double[] first, double[] second)
    {
        if (first.Length != 3 || second.Length != 3)
        {
            throw new ArgumentException("MathNetOracle3D requires three-dimensional coordinate arrays.");
        }

        return MathNet.Numerics.Distance.Euclidean(first, second);
    }

    public static double Distance(GeoPoint3D first, double[] second)
    {
        return Distance(new[] { first.X, first.Y, first.Z }, second);
    }

    public static double Distance(double[] first, GeoPoint3D second)
    {
        return Distance(first, new[] { second.X, second.Y, second.Z });
    }
}
