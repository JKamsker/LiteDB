namespace LiteDB.Spatial;

/// <summary>
/// Computes distances between spatial points for exact filtering.
/// </summary>
public interface ISpatialDistance
{
    /// <summary>
    /// Computes the distance between two geographic points expressed in meters.
    /// </summary>
    double Distance(global::LiteDB.Spatial.GeoPoint left, global::LiteDB.Spatial.GeoPoint right);

    /// <summary>
    /// Computes the distance between two Cartesian points expressed in user-defined units.
    /// </summary>
    double Distance(global::LiteDB.Spatial.GeoPoint3D left, global::LiteDB.Spatial.GeoPoint3D right);
}
