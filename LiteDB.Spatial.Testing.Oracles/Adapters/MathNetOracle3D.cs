using LiteDB.Spatial.Testing.Oracles.Infrastructure;
using LiteDB.Spatial.Testing.Oracles.Interfaces;
using MathNet.Spatial.Euclidean;

namespace LiteDB.Spatial.Testing.Oracles.Adapters;

/// <summary>
/// Thin wrapper around MathNet.Spatial distance operations.
/// </summary>
public sealed class MathNetOracle3D : IDistanceOracle3D
{
    private const string OracleKey = "mathnet";

    /// <inheritdoc />
    public bool IsAvailable => OracleEnvironment.IsOracleEnabled(OracleKey);

    /// <inheritdoc />
    public double Distance(CartesianCoordinate3D left, CartesianCoordinate3D right)
    {
        OracleEnvironment.EnsureOraclesEnabled(OracleKey);
        var leftPoint = new Point3D(left.X, left.Y, left.Z);
        var rightPoint = new Point3D(right.X, right.Y, right.Z);
        return leftPoint.DistanceTo(rightPoint);
    }
}
