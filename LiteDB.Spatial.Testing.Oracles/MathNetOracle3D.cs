using System;
using MathNetPoint3D = MathNet.Spatial.Euclidean.Point3D;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// MathNet.Spatial-backed Euclidean 3D distance oracle.
/// </summary>
public sealed class MathNetOracle3D : IDistanceOracle3D
{
    private readonly bool _enabled;

    public MathNetOracle3D()
    {
        _enabled = OracleEnvironment.Allows("mathnet");
    }

    public string Name => "MathNet.Spatial";

    public bool IsEnabled => _enabled;

    public double Distance(Point3D first, Point3D second)
    {
        EnsureEnabled();
        var a = new MathNetPoint3D(first.X, first.Y, first.Z);
        var b = new MathNetPoint3D(second.X, second.Y, second.Z);
        return a.DistanceTo(b);
    }

    private void EnsureEnabled()
    {
        if (!_enabled)
        {
            throw new InvalidOperationException("MathNet.Spatial oracle is disabled by SPATIAL_ORACLES.");
        }
    }
}
