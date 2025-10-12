using System;
using GeoJSON.Net.Geometry;
using LiteDB.Spatial.Testing.Oracles.Abstractions;

namespace LiteDB.Spatial.Testing.Oracles.Adapters
{
    public sealed class PostgisOracle : IGeometryOracle2D, IDistanceOracle3D
    {
        public string Name => OracleNames.Postgis;

        public bool IsAvailable => false;

        public double ComputeArea(Polygon polygon)
        {
            throw new NotSupportedException("PostGIS verification is only available when connected to a live database environment.");
        }

        public double ComputePerimeter(Polygon polygon)
        {
            throw new NotSupportedException("PostGIS verification is only available when connected to a live database environment.");
        }

        public double Distance(CartesianPoint a, CartesianPoint b)
        {
            throw new NotSupportedException("PostGIS verification is only available when connected to a live database environment.");
        }
    }
}
