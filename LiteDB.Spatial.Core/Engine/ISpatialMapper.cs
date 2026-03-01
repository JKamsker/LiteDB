extern alias LiteDbBase;

using BaseLiteDB = LiteDbBase::LiteDB;

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

    /// <summary>
    /// Attempts to extract a two-dimensional point from the provided document for indexing purposes.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="point">When this method returns, contains the extracted point if available.</param>
    /// <returns><c>true</c> when a point could be extracted; otherwise, <c>false</c>.</returns>
    bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point);

    /// <summary>
    /// Attempts to extract a three-dimensional point from the provided document for indexing purposes.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="point">When this method returns, contains the extracted point if available.</param>
    /// <returns><c>true</c> when a point could be extracted; otherwise, <c>false</c>.</returns>
    bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point);
}
