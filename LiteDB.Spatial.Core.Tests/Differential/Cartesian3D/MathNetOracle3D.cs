using MathNet.Spatial.Euclidean;

namespace LiteDB.Spatial.Core.Tests.Differential.Cartesian3D;

internal static class MathNetOracle3D
{
    public static double Distance((double X, double Y, double Z) left, (double X, double Y, double Z) right)
    {
        var a = new Point3D(left.X, left.Y, left.Z);
        var b = new Point3D(right.X, right.Y, right.Z);
        return a.DistanceTo(b);
    }
}
