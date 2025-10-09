extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

#nullable enable

/// <summary>
/// Facade for configuring and querying three-dimensional Cartesian spatial data.
/// </summary>
public static class SpatialCartesian3D
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
            Cartesian3DEngine.EngineName,
            3,
            geometryFieldName,
            resolvedOptions);

        var store = new SpatialMetadataStore(database);
        store.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(resolvedOptions.IndexFieldName, BaseLiteDB.BsonExpression.Create("$." + resolvedOptions.IndexFieldName));
        collection.EnsureIndex(resolvedOptions.BoundingBoxFieldName, BaseLiteDB.BsonExpression.Create("$." + resolvedOptions.BoundingBoxFieldName));

        return descriptor.WithEngine(new Cartesian3DEngine(geometryFieldName, resolvedOptions));
    }

    public static ISpatialQueryPlan Near(SpatialCollectionDescriptor descriptor, GeoPoint3D center, double radius)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (descriptor.Dimensions != 3)
        {
            throw new SpatialMetadataException("Descriptor does not describe a 3D collection.");
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

        if (bounds.Dimensions != 3)
        {
            throw new ArgumentException("Bounding boxes must contain six values for 3D queries.", nameof(bounds));
        }

        var engine = ResolveEngine(descriptor);
        return engine.PlanWithin(bounds);
    }

    private static Cartesian3DEngine ResolveEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.Engine is Cartesian3DEngine engine)
        {
            return engine;
        }

        return new Cartesian3DEngine(descriptor.GeometryFieldName, descriptor.Options);
    }
}
