extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers that configure collections for geographic indexing and produce query plans.
/// </summary>
public static class SpatialGeographic
{
    private const string MissingConfigurationMessage = "Call SpatialGeographic.EnsurePointIndex before issuing geographic queries.";

    /// <summary>
    /// Configures the specified collection for geographic point indexing.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode mode = GeographicDistanceMode.Vincenty)
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

        var metadata = new SpatialMetadataStore(database);
        metadata.SaveDescriptor(collectionName, descriptor);

        var collection = database.GetCollection(collectionName);
        collection.EnsureIndex(engine.Options.IndexFieldName, BaseLiteDB.BsonExpression.Create("$." + engine.Options.IndexFieldName));

        return descriptor;
    }

    /// <summary>
    /// Produces a geographic near query plan using persisted metadata.
    /// </summary>
    public static ISpatialQueryPlan Near(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        GeoPoint center,
        double radius,
        GeographicDistanceMode mode = GeographicDistanceMode.Vincenty)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingConfigurationMessage);
        var engine = ResolveEngine(descriptor, mode);
        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Produces a geographic bounding box query plan using persisted metadata.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        BoundingBox bounds,
        GeographicDistanceMode mode = GeographicDistanceMode.Vincenty)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingConfigurationMessage);
        var engine = ResolveEngine(descriptor, mode);
        return engine.PlanWithin(bounds);
    }

    private static GeographicEngine ResolveEngine(SpatialCollectionDescriptor descriptor, GeographicDistanceMode mode)
    {
        if (descriptor.Engine is GeographicEngine geographic)
        {
            return geographic;
        }

        return new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options, mode);
    }
}
