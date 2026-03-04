namespace LiteDB.Spatial;

/// <summary>
/// Computes distances between spatial points for exact filtering.
/// </summary>
public interface ISpatialDistance
{
    /// <summary>
    /// Computes the distance between two geographic points expressed in meters.
    /// </summary>
    double Distance(GeoPoint left, GeoPoint right);

    /// <summary>
    /// Computes the distance between two Cartesian points expressed in user-defined units.
    /// </summary>
    double Distance(GeoPoint3D left, GeoPoint3D right);
}
