extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

public static class SpatialCartesian2D
{
    public static Cartesian2DEngine CreateEngine(string geometryField, SpatialIndexOptions? options = null)
    {
        return new Cartesian2DEngine(geometryField, options);
    }

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
        var engine = new Cartesian2DEngine(geometryField, resolvedOptions);
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

    public static ISpatialQueryPlan Near(Cartesian2DEngine engine, GeoPoint center, double radius)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanNear(center, radius);
    }

    public static ISpatialQueryPlan WithinBoundingBox(Cartesian2DEngine engine, BoundingBox bounds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanWithin(bounds);
    }
}
