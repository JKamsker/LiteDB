extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Convenience helpers for configuring and querying geographic collections.
/// </summary>
public static class SpatialGeographic
{
    /// <summary>
    /// Creates a geographic engine bound to the specified geometry field.
    /// </summary>
    public static GeographicEngine CreateEngine(string geometryField, SpatialIndexOptions? options = null)
    {
        return new GeographicEngine(geometryField, options);
    }

    /// <summary>
    /// Ensures a point-based geographic index exists for the collection, persisting metadata and backfilling documents.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryField,
        SpatialIndexOptions? options = null)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        var resolvedOptions = options ?? new SpatialIndexOptions();
        var engine = new GeographicEngine(geometryField, resolvedOptions);
        var descriptor = new SpatialCollectionDescriptor(
            collectionName,
            engine.Name,
            engine.Dimensions,
            string.IsNullOrWhiteSpace(geometryField) ? SpatialCollectionDescriptor.DefaultGeometryFieldName : geometryField,
            resolvedOptions).WithEngine(engine);

        var store = new SpatialMetadataStore(database);
        store.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        SpatialBackfill.Run(collection, descriptor, engine);

        return descriptor;
    }

    /// <summary>
    /// Creates a near query plan using the provided engine instance.
    /// </summary>
    public static ISpatialQueryPlan Near(GeographicEngine engine, GeoPoint center, double radiusMeters)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanNear(center, radiusMeters);
    }

    /// <summary>
    /// Creates a bounding-box query plan using the provided engine instance.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(GeographicEngine engine, BoundingBox bounds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanWithin(bounds);
    }
}
