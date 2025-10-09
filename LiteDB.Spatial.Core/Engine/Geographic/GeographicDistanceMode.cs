namespace LiteDB.Spatial;

/// <summary>
/// Enumerates the supported distance calculation strategies for geographic coordinates.
/// </summary>
public enum GeographicDistanceMode
{
    /// <summary>
    /// Uses the haversine formula for spherical distance approximations.
    /// </summary>
    Haversine,

    /// <summary>
    /// Uses the Vincenty inverse formula for ellipsoidal distance calculations.
    /// </summary>
    Vincenty
}
