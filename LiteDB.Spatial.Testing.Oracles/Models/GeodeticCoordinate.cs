namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Represents a WGS84 geodetic coordinate in degrees.
/// </summary>
/// <param name="Latitude">Latitude in degrees.</param>
/// <param name="Longitude">Longitude in degrees.</param>
public readonly record struct GeodeticCoordinate(double Latitude, double Longitude);
