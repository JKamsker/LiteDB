#nullable enable

using System;
using LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the runtime configuration required to populate spatial index fields for a collection.
/// </summary>
public sealed class SpatialCollectionDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCollectionDescriptor"/> class.
    /// </summary>
    public SpatialCollectionDescriptor(
        SpatialCollectionMetadata metadata,
        ISpatialMapper mapper,
        Func<BsonDocument, GeoPoint?>? pointAccessor,
        Func<BsonDocument, GeoPoint3D?>? point3DAccessor)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        PointAccessor = pointAccessor;
        Point3DAccessor = point3DAccessor;

        if (metadata.Dimensions == 2 && pointAccessor is null)
        {
            throw new ArgumentException("A two-dimensional collection descriptor requires a GeoPoint accessor.", nameof(pointAccessor));
        }

        if (metadata.Dimensions == 3 && point3DAccessor is null)
        {
            throw new ArgumentException("A three-dimensional collection descriptor requires a GeoPoint3D accessor.", nameof(point3DAccessor));
        }
    }

    /// <summary>
    /// Gets the persisted metadata for the collection.
    /// </summary>
    public SpatialCollectionMetadata Metadata { get; }

    /// <summary>
    /// Gets the mapper responsible for computing index encodings and bounding boxes.
    /// </summary>
    public ISpatialMapper Mapper { get; }

    /// <summary>
    /// Gets the delegate used to retrieve a two-dimensional point from a document when <see cref="SpatialCollectionMetadata.Dimensions"/> equals two.
    /// </summary>
    public Func<BsonDocument, GeoPoint?>? PointAccessor { get; }

    /// <summary>
    /// Gets the delegate used to retrieve a three-dimensional point from a document when <see cref="SpatialCollectionMetadata.Dimensions"/> equals three.
    /// </summary>
    public Func<BsonDocument, GeoPoint3D?>? Point3DAccessor { get; }

    /// <summary>
    /// Gets the number of values expected within the bounding box field.
    /// </summary>
    public int BoundingBoxElementCount => Metadata.BoundingBoxElementCount;
}
