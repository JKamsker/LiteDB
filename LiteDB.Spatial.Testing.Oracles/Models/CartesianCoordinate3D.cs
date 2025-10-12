namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Represents a three-dimensional Cartesian coordinate.
/// </summary>
/// <param name="X">The X component.</param>
/// <param name="Y">The Y component.</param>
/// <param name="Z">The Z component.</param>
public readonly record struct CartesianCoordinate3D(double X, double Y, double Z);
