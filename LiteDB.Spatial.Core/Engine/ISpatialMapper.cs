extern alias litedb;
using LiteDbRuntime = litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Projects application-level spatial values into indexable representations.
/// </summary>
public interface ISpatialMapper
{
    /// <summary>
    /// Computes the bounding box for the provided two-dimensional point.
    /// </summary>
    BoundingBox GetBoundingBox(global::LiteDB.Spatial.GeoPoint point);

    /// <summary>
    /// Computes the bounding box for the provided three-dimensional point.
    /// </summary>
    BoundingBox GetBoundingBox(global::LiteDB.Spatial.GeoPoint3D point);

    /// <summary>
    /// Encodes a two-dimensional point into the spatial index key space.
    /// </summary>
    ulong Encode(global::LiteDB.Spatial.GeoPoint point);

    /// <summary>
    /// Encodes a three-dimensional point into the spatial index key space.
    /// </summary>
    ulong Encode(global::LiteDB.Spatial.GeoPoint3D point);

    /// <summary>
    /// Attempts to map the provided document into index fields using the configured options.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="options">The spatial index options for the collection.</param>
    /// <param name="index">When this method returns, contains the encoded index value.</param>
    /// <param name="boundingBox">When this method returns, contains the computed bounding box.</param>
    /// <returns><c>true</c> if the document contains a spatial value that can be indexed; otherwise, <c>false</c>.</returns>
    bool TryMapDocument(LiteDbRuntime.BsonDocument document, SpatialIndexOptions options, out ulong index, out BoundingBox boundingBox);
}
