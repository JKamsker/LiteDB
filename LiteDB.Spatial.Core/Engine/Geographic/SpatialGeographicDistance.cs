#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Provides standalone helpers for computing geographic distances.
/// </summary>
public static class SpatialGeographicDistance
{
    /// <summary>
    /// Computes the great-circle distance in meters between two coordinates.
    /// </summary>
    /// <param name="longitude1">Longitude of the first point in decimal degrees.</param>
    /// <param name="latitude1">Latitude of the first point in decimal degrees.</param>
    /// <param name="longitude2">Longitude of the second point in decimal degrees.</param>
    /// <param name="latitude2">Latitude of the second point in decimal degrees.</param>
    /// <returns>The distance in meters along the surface of the Earth.</returns>
    public static double Haversine(double longitude1, double latitude1, double longitude2, double latitude2)
    {
        return GeographicMath.HaversineDistance(longitude1, latitude1, longitude2, latitude2);
    }
}
