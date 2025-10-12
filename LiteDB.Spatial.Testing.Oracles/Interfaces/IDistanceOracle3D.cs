namespace LiteDB.Spatial.Testing.Oracles.Interfaces;

/// <summary>
/// Provides reference implementations for Euclidean 3D distance checks.
/// </summary>
public interface IDistanceOracle3D
{
    /// <summary>
    /// Gets a value indicating whether the oracle is available in the current environment.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Computes the Euclidean distance between two Cartesian coordinates.
    /// </summary>
    double Distance(CartesianCoordinate3D left, CartesianCoordinate3D right);
}
