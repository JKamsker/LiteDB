extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

#nullable enable

/// <summary>
/// Facade for configuring and planning Cartesian 2D spatial queries.
/// </summary>
public static class SpatialCartesian2D
{
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
            Cartesian2DEngine.EngineName,
            2,
            geometryFieldName,
            resolvedOptions);

        var store = new SpatialMetadataStore(database);
        store.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(resolvedOptions.IndexFieldName, BaseLiteDB.BsonExpression.Create("$." + resolvedOptions.IndexFieldName));
        collection.EnsureIndex(resolvedOptions.BoundingBoxFieldName, BaseLiteDB.BsonExpression.Create("$." + resolvedOptions.BoundingBoxFieldName));

        return descriptor.WithEngine(new Cartesian2DEngine(geometryFieldName, resolvedOptions));
    }

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

    public static ISpatialQueryPlan WithinBoundingBox(SpatialCollectionDescriptor descriptor, BoundingBox bounds)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian 2D bounding boxes must contain four values.", nameof(bounds));
        }

        var engine = ResolveEngine(descriptor);
        return engine.PlanWithin(bounds);
    }

    private static Cartesian2DEngine ResolveEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.Engine is Cartesian2DEngine engine)
        {
            return engine;
        }

        return new Cartesian2DEngine(descriptor.GeometryFieldName, descriptor.Options);
    }
}
