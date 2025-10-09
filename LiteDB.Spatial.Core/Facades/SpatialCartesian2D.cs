extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Facade for configuring and querying two-dimensional Cartesian collections.
/// </summary>
public static class SpatialCartesian2D
{
    /// <summary>
    /// Configures a collection for 2D Cartesian indexing.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryFieldName,
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

        var engine = new Cartesian2DEngine(geometryFieldName, options);
        var descriptor = new SpatialCollectionDescriptor(collectionName, engine.Name, engine.Dimensions, geometryFieldName, engine.Options, engine);

        var store = new SpatialMetadataStore(database);
        store.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(engine.Options.IndexFieldName);

        return descriptor;
    }

    /// <summary>
    /// Creates a near query plan using Euclidean distance.
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
    /// Creates a query plan for values within an axis-aligned bounding box.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        BoundingBox bounds)
    {
        var engine = ResolveEngine(database, collectionName);
        return engine.PlanWithin(bounds);
    }

    private static Cartesian2DEngine ResolveEngine(BaseLiteDB.ILiteDatabase database, string collectionName)
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
        var descriptor = store.GetRequiredDescriptor(collectionName, "Call SpatialCartesian2D.EnsurePointIndex first.");
        return Cartesian2DEngine.FromDescriptor(descriptor);
    }
}

