extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Provides convenience helpers for configuring and querying geographic collections.
/// </summary>
public static class SpatialGeographic
{
    /// <summary>
    /// Configures the supplied collection for geographic point indexing.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryFieldName,
        GeographicDistanceMode mode = GeographicDistanceMode.Haversine,
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

        var engine = new GeographicEngine(geometryFieldName, options, mode);
        var descriptor = new SpatialCollectionDescriptor(collectionName, engine.Name, engine.Dimensions, geometryFieldName, engine.Options, engine);

        var store = new SpatialMetadataStore(database);
        store.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(engine.Options.IndexFieldName);

        return descriptor;
    }

    /// <summary>
    /// Creates a query plan for documents near the provided geographic coordinate.
    /// </summary>
    public static ISpatialQueryPlan Near(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        GeoPoint center,
        double radius)
    {
        var engine = ResolveEngine(database, collectionName);
        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Creates a query plan for values contained within the provided bounding box.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        BoundingBox bounds)
    {
        var engine = ResolveEngine(database, collectionName);
        return engine.PlanWithin(bounds);
    }

    private static GeographicEngine ResolveEngine(BaseLiteDB.ILiteDatabase database, string collectionName)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        var store = new SpatialMetadataStore(database);
        var descriptor = store.GetRequiredDescriptor(collectionName, "Call SpatialGeographic.EnsurePointIndex first.");

        return GeographicEngine.FromDescriptor(descriptor);
    }
}

