namespace LiteDB.Spatial.Testing.Oracles.Interfaces;

/// <summary>
/// Provides a reference implementation for geodesic calculations over the WGS84 ellipsoid.
/// </summary>
public interface IGeodesicOracle
{
    /// <summary>
    /// Gets a value indicating whether the oracle is available in the current environment.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Computes the geodesic distance between two coordinates in meters.
    /// </summary>
    /// <param name="from">The starting coordinate.</param>
    /// <param name="to">The ending coordinate.</param>
    /// <returns>The geodesic distance in meters.</returns>
    double DistanceInMeters(GeodeticCoordinate from, GeodeticCoordinate to);
}
