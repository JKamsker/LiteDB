extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

#nullable enable

/// <summary>
/// Provides helpers for configuring and planning geographic spatial queries.
/// </summary>
public static class SpatialGeographic
{
    /// <summary>
    /// Configures a collection for geographic indexing and persists metadata describing the engine.
    /// </summary>
    /// <param name="database">The database hosting the collection.</param>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="geometryFieldName">The field that stores geographic points.</param>
    /// <param name="options">Optional index configuration.</param>
    /// <returns>A descriptor describing the spatial configuration.</returns>
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

        var resolvedOptions = options ?? new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor(
            collectionName,
            GeographicEngine.EngineName,
            2,
            geometryFieldName,
            resolvedOptions);

        var store = new SpatialMetadataStore(database);
        store.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(resolvedOptions.IndexFieldName, BaseLiteDB.BsonExpression.Create("$." + resolvedOptions.IndexFieldName));
        collection.EnsureIndex(resolvedOptions.BoundingBoxFieldName, BaseLiteDB.BsonExpression.Create("$." + resolvedOptions.BoundingBoxFieldName));

        return descriptor.WithEngine(new GeographicEngine(geometryFieldName, resolvedOptions));
    }

    /// <summary>
    /// Builds an index-aware near query plan using the stored descriptor.
    /// </summary>
    public static ISpatialQueryPlan Near(SpatialCollectionDescriptor descriptor, GeoPoint center, double radius)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (descriptor.Dimensions != 2)
        {
            throw new SpatialMetadataException("Descriptor does not describe a 2D collection.");
        }

        var engine = ResolveEngine(descriptor);
        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Builds an index-aware bounding box query plan using the stored descriptor.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(SpatialCollectionDescriptor descriptor, BoundingBox bounds)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Bounding boxes must be two-dimensional for geographic queries.", nameof(bounds));
        }

        var engine = ResolveEngine(descriptor);
        return engine.PlanWithin(bounds);
    }

    private static GeographicEngine ResolveEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.Engine is GeographicEngine geographic)
        {
            return geographic;
        }

        return new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options);
    }
}
