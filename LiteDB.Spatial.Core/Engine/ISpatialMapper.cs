namespace LiteDB.Spatial;

/// <summary>
/// Projects application-level spatial values into indexable representations.
/// </summary>
public interface ISpatialMapper
{
    /// <summary>
    /// Computes the bounding box for the provided two-dimensional point.
    /// </summary>
    BoundingBox GetBoundingBox(GeoPoint point);

    /// <summary>
    /// Computes the bounding box for the provided three-dimensional point.
    /// </summary>
    BoundingBox GetBoundingBox(GeoPoint3D point);

    /// <summary>
    /// Encodes a two-dimensional point into the spatial index key space.
    /// </summary>
    ulong Encode(GeoPoint point);

    /// <summary>
    /// Encodes a three-dimensional point into the spatial index key space.
    /// </summary>
    ulong Encode(GeoPoint3D point);
}
