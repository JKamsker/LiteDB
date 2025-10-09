namespace LiteDB.Spatial;

/// <summary>
/// Defines the distance algorithm used by the geographic engine.
/// </summary>
public enum GeographicDistanceMode
{
    /// <summary>
    /// Uses the great-circle distance assuming a spherical Earth.
    /// </summary>
    Haversine,

    /// <summary>
    /// Uses Vincenty's inverse formula over the WGS84 ellipsoid.
    /// </summary>
    Vincenty
}
