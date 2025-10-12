using System;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using MathNet.Spatial.Euclidean;

namespace LiteDB.Spatial.Testing.Oracles.Oracles;

public sealed class MathNetOracle3D : OracleBase, IDistanceOracle3D
{
    public MathNetOracle3D()
        : base("mathnet", isDatabaseOracle: false)
    {
    }

    public double GetDistance(GeoPoint3D first, GeoPoint3D second)
    {
        EnsureAvailable();
        var a = new Point3D(first.X, first.Y, first.Z);
        var b = new Point3D(second.X, second.Y, second.Z);
        return a.DistanceTo(b);
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(SkipReason ?? "MathNet oracle is disabled");
        }
    }
}
