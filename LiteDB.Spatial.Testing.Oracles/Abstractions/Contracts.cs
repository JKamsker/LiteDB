using GeoJSON.Net.Geometry;

namespace LiteDB.Spatial.Testing.Oracles.Abstractions
{
    public interface IOracle
    {
        string Name { get; }

        bool IsAvailable { get; }
    }

    public interface IGeodesicOracle : IOracle
    {
        double DistanceMeters(GeoCoordinate start, GeoCoordinate end);
    }

    public interface IGeometryOracle2D : IOracle
    {
        double ComputeArea(Polygon polygon);

        double ComputePerimeter(Polygon polygon);
    }

    public interface IDistanceOracle3D : IOracle
    {
        double Distance(CartesianPoint a, CartesianPoint b);
    }

    public readonly record struct GeoCoordinate(double Latitude, double Longitude);

    public readonly record struct CartesianPoint(double X, double Y, double Z);
}
