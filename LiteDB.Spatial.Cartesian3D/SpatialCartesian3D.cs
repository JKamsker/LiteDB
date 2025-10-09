extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

public static class SpatialCartesian3D
{
    public static Cartesian3DEngine CreateEngine(string geometryField, SpatialIndexOptions? options = null)
    {
        return new Cartesian3DEngine(geometryField, options);
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
        var engine = new Cartesian3DEngine(geometryField, resolvedOptions);
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

    public static ISpatialQueryPlan Near(Cartesian3DEngine engine, GeoPoint3D center, double radius)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanNear(center, radius);
    }

    public static ISpatialQueryPlan WithinBoundingBox(Cartesian3DEngine engine, BoundingBox bounds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanWithin(bounds);
    }
}
