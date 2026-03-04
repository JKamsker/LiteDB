extern alias LiteDbBase;

#nullable enable

using System;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Plugin.Runtime
{
    internal static class SpatialInitializer
    {
        public static SpatialCollectionDescriptor EnsureGeographic(
            BaseLiteDB.LiteDatabase database,
            SpatialMetadataStore metadata,
            string collectionName,
            string geometryFieldName,
            SpatialIndexOptions? options,
            GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
            if (string.IsNullOrWhiteSpace(geometryFieldName)) throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));

            var bsonCollection = database.GetCollection(collectionName);
            var descriptor = SpatialGeographic.EnsurePointIndex(
                metadata,
                bsonCollection,
                geometryFieldName,
                options,
                distanceMode);

            return descriptor;
        }

        public static SpatialCollectionDescriptor EnsureCartesian2D(
            BaseLiteDB.LiteDatabase database,
            SpatialMetadataStore metadata,
            string collectionName,
            string geometryFieldName,
            BoundingBox domain,
            SpatialIndexOptions? options)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
            if (string.IsNullOrWhiteSpace(geometryFieldName)) throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));

            var bsonCollection = database.GetCollection(collectionName);
            var descriptor = SpatialCartesian2D.EnsurePointIndex(
                metadata,
                bsonCollection,
                geometryFieldName,
                domain,
                options);

            return descriptor;
        }

        public static SpatialCollectionDescriptor EnsureCartesian3D(
            BaseLiteDB.LiteDatabase database,
            SpatialMetadataStore metadata,
            string collectionName,
            string geometryFieldName,
            BoundingBox domain,
            SpatialIndexOptions? options)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
            if (string.IsNullOrWhiteSpace(geometryFieldName)) throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));

            var bsonCollection = database.GetCollection(collectionName);
            var descriptor = SpatialCartesian3D.EnsurePointIndex(
                metadata,
                bsonCollection,
                geometryFieldName,
                domain,
                options);

            return descriptor;
        }
    }
}
