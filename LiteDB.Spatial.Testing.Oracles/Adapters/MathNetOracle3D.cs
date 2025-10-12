using LiteDB.Spatial.Testing.Oracles.Abstractions;
using MathNet.Spatial.Euclidean;

namespace LiteDB.Spatial.Testing.Oracles.Adapters
{
    public sealed class MathNetOracle3D : IDistanceOracle3D
    {
        public string Name => OracleNames.MathNet3D;

        public bool IsAvailable => true;

        public double Distance(CartesianPoint a, CartesianPoint b)
        {
            var start = new Point3D(a.X, a.Y, a.Z);
            var end = new Point3D(b.X, b.Y, b.Z);
            return start.DistanceTo(end);
        }
    }
}
