using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using LiteDB.Spatial.Testing.Oracles.Adapters;
using LiteDB.Spatial.Testing.Oracles.Internal;

namespace LiteDB.Spatial.Testing.Oracles
{
    public static class SpatialOracleCatalog
    {
        private static readonly IReadOnlyList<IOracle> AllOracles = new ReadOnlyCollection<IOracle>(new IOracle[]
        {
            new GeographicLibOracle(),
            new NtsOracle(),
            new MathNetOracle3D(),
            new PostgisOracle()
        });

        public static IEnumerable<IOracle> All => AllOracles;

        public static IEnumerable<IGeodesicOracle> Geodesic => AllOfType<IGeodesicOracle>();

        public static IEnumerable<IGeometryOracle2D> Geometry2D => AllOfType<IGeometryOracle2D>();

        public static IEnumerable<IDistanceOracle3D> Distance3D => AllOfType<IDistanceOracle3D>();

        public static bool ShouldExecute(IOracle oracle)
        {
            return oracle.IsAvailable && OracleEnvironment.RequiresOracle(oracle.Name);
        }

        private static IEnumerable<T> AllOfType<T>() where T : class, IOracle
        {
            return AllOracles.OfType<T>();
        }
    }
}
