#nullable enable

using LiteDB.Spatial;
using MathNet.Numerics;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

internal static class MathNetOracle3D
{
    public static double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var a = new[] { left.X, left.Y, left.Z };
        var b = new[] { right.X, right.Y, right.Z };
        return MathNet.Numerics.Distance.Euclidean(a, b);
    }
}
