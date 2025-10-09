namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Provides high-precision geodesic distances on Earth.
/// </summary>
public interface IGeodesicOracle
{
    string Name { get; }

    bool IsEnabled { get; }

    double DistanceMeters(GeoCoordinate start, GeoCoordinate end);
}
