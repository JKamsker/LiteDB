extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers for configuring flat two-dimensional spatial collections.
/// </summary>
public static class SpatialCartesian2D
{
    private const string MissingConfigurationMessage = "Call SpatialCartesian2D.EnsurePointIndex before issuing 2D spatial queries.";

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

        var metadata = new SpatialMetadataStore(database);
        metadata.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(engine.Options.IndexFieldName, BaseLiteDB.BsonExpression.Create("$." + engine.Options.IndexFieldName));

        return descriptor;
    }

    public static ISpatialQueryPlan Near(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        GeoPoint center,
        double radius)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingConfigurationMessage);
        var engine = ResolveEngine(descriptor);
        return engine.PlanNear(center, radius);
    }

    public static ISpatialQueryPlan WithinBoundingBox(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        BoundingBox bounds)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingConfigurationMessage);
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
