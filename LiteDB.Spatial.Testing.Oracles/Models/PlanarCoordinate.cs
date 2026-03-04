namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Represents a two-dimensional planar coordinate.
/// </summary>
/// <param name="X">The X component.</param>
/// <param name="Y">The Y component.</param>
public readonly record struct PlanarCoordinate(double X, double Y);
