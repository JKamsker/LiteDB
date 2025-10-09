extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides the top-level entry point for configuring and dispatching spatial queries.
/// </summary>
public static class Spatial
{
    private const string MissingMetadataHint = "Configure the collection using Spatial.UseGeographic/UseCartesian2D/UseCartesian3D before invoking spatial helpers.";

    /// <summary>
    /// Configures a collection to use the geographic engine and persists the descriptor in the metadata store.
    /// </summary>
    public static SpatialCollectionDescriptor UseGeographic(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var store = new SpatialMetadataStore(database);
        return UseGeographic(store, collectionName, geometryFieldName, options, distanceMode);
    }

    /// <summary>
    /// Configures a collection to use the geographic engine and persists the descriptor in the metadata store.
    /// </summary>
    public static SpatialCollectionDescriptor UseGeographic(
        SpatialMetadataStore metadata,
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        var descriptor = new SpatialCollectionDescriptor(
            collectionName,
            GeographicEngine.EngineName,
            dimensions: 2,
            geometryFieldName,
            options ?? new SpatialIndexOptions(),
            SpatialEngineSettings.ForGeographic(distanceMode));

        metadata.SaveDescriptor(collectionName, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures a collection to use the 2D Cartesian engine and persists the descriptor in the metadata store.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian2D(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var store = new SpatialMetadataStore(database);
        return UseCartesian2D(store, collectionName, geometryFieldName, domain, options);
    }

    /// <summary>
    /// Configures a collection to use the 2D Cartesian engine and persists the descriptor in the metadata store.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian2D(
        SpatialMetadataStore metadata,
        string collectionName,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (domain.Dimensions != 2)
        {
            throw new ArgumentException("Cartesian2D engines require a two-dimensional domain.", nameof(domain));
        }

        var descriptor = new SpatialCollectionDescriptor(
            collectionName,
            Cartesian2DEngine.EngineName,
            dimensions: 2,
            geometryFieldName,
            options ?? new SpatialIndexOptions(),
            SpatialEngineSettings.ForCartesian(domain));

        metadata.SaveDescriptor(collectionName, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Configures a collection to use the 3D Cartesian engine and persists the descriptor in the metadata store.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian3D(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var store = new SpatialMetadataStore(database);
        return UseCartesian3D(store, collectionName, geometryFieldName, domain, options);
    }

    /// <summary>
    /// Configures a collection to use the 3D Cartesian engine and persists the descriptor in the metadata store.
    /// </summary>
    public static SpatialCollectionDescriptor UseCartesian3D(
        SpatialMetadataStore metadata,
        string collectionName,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D engines require a three-dimensional domain.", nameof(domain));
        }

        var descriptor = new SpatialCollectionDescriptor(
            collectionName,
            Cartesian3DEngine.EngineName,
            dimensions: 3,
            geometryFieldName,
            options ?? new SpatialIndexOptions(),
            SpatialEngineSettings.ForCartesian(domain));

        metadata.SaveDescriptor(collectionName, descriptor);
        return descriptor;
    }

    /// <summary>
    /// Ensures the spatial index for the specified collection, creating computed fields and backfilling missing values.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(
        BaseLiteDB.ILiteDatabase database,
        string collectionName)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        var collection = database.GetCollection(collectionName);
        return EnsurePointIndex(metadata, collection);
    }

    /// <summary>
    /// Ensures the spatial index for the specified collection, creating computed fields and backfilling missing values.
    /// </summary>
    public static SpatialCollectionDescriptor EnsurePointIndex(
        SpatialMetadataStore metadata,
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        var descriptor = metadata.GetRequiredDescriptor(collection.Name, MissingMetadataHint);
        return EnsurePointIndex(metadata, collection, descriptor);
    }

    /// <summary>
    /// Creates a query plan that selects points near the provided geographic center.
    /// </summary>
    public static ISpatialQueryPlan Near(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        GeoPoint center,
        double radius,
        GeographicDistanceMode? distanceMode = null)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        return Near(metadata, collectionName, center, radius, distanceMode);
    }

    /// <summary>
    /// Creates a query plan that selects points near the provided geographic center.
    /// </summary>
    public static ISpatialQueryPlan Near(
        SpatialMetadataStore metadata,
        string collectionName,
        GeoPoint center,
        double radius,
        GeographicDistanceMode? distanceMode = null)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingMetadataHint);
        return Near(descriptor, center, radius, distanceMode);
    }

    /// <summary>
    /// Creates a query plan that selects points near the provided geographic center.
    /// </summary>
    public static ISpatialQueryPlan Near(
        SpatialCollectionDescriptor descriptor,
        GeoPoint center,
        double radius,
        GeographicDistanceMode? distanceMode = null)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (descriptor.Dimensions != 2)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D but a 2D near query was requested.");
        }

        var engine = GetEngine(descriptor, distanceMode);
        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Creates a query plan that selects three-dimensional points near the provided center.
    /// </summary>
    public static ISpatialQueryPlan Near(
        BaseLiteDB.ILiteDatabase database,
        string collectionName,
        GeoPoint3D center,
        double radius)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var metadata = new SpatialMetadataStore(database);
        return Near(metadata, collectionName, center, radius);
    }

    /// <summary>
    /// Creates a query plan that selects three-dimensional points near the provided center.
    /// </summary>
    public static ISpatialQueryPlan Near(
        SpatialMetadataStore metadata,
        string collectionName,
        GeoPoint3D center,
        double radius)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingMetadataHint);
        return Near(descriptor, center, radius);
    }

    /// <summary>
    /// Creates a query plan that selects three-dimensional points near the provided center.
    /// </summary>
    public static ISpatialQueryPlan Near(
        SpatialCollectionDescriptor descriptor,
        GeoPoint3D center,
        double radius)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (descriptor.Dimensions != 3)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Dimensions}D but a 3D near query was requested.");
        }

        var engine = GetEngine(descriptor);
        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Creates a query plan that selects documents contained within the specified bounding box.
    /// </summary>
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
        return WithinBoundingBox(metadata, collectionName, bounds);
    }

    /// <summary>
    /// Creates a query plan that selects documents contained within the specified bounding box.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(
        SpatialMetadataStore metadata,
        string collectionName,
        BoundingBox bounds)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var descriptor = metadata.GetRequiredDescriptor(collectionName, MissingMetadataHint);
        return WithinBoundingBox(descriptor, bounds);
    }

    /// <summary>
    /// Creates a query plan that selects documents contained within the specified bounding box.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(
        SpatialCollectionDescriptor descriptor,
        BoundingBox bounds)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        descriptor.EnsureCompatible(bounds);
        var engine = GetEngine(descriptor);
        return engine.PlanWithin(bounds);
    }

    private static SpatialCollectionDescriptor EnsurePointIndex(
        SpatialMetadataStore metadata,
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        SpatialCollectionDescriptor descriptor)
    {
        var engine = GetEngine(descriptor);
        var descriptorWithEngine = descriptor.Engine == engine ? descriptor : descriptor.WithEngine(engine);

        EnsureIndexes(collection, descriptorWithEngine);
        SpatialBackfill.Run(collection, descriptorWithEngine, engine);

        return descriptorWithEngine;
    }

    private static ISpatialEngine GetEngine(SpatialCollectionDescriptor descriptor, GeographicDistanceMode? requestedMode = null)
    {
        if (descriptor.Engine is ISpatialEngine existingEngine)
        {
            if (TryValidateEngine(descriptor, existingEngine, requestedMode))
            {
                return existingEngine;
            }
        }
        else if (descriptor.HasEngineFactory && descriptor.TryGetEngine(out var materialized) && TryValidateEngine(descriptor, materialized, requestedMode))
        {
            return materialized;
        }

        return descriptor.EngineName switch
        {
            GeographicEngine.EngineName => CreateGeographicEngine(descriptor, requestedMode),
            Cartesian2DEngine.EngineName => CreateCartesian2DEngine(descriptor),
            Cartesian3DEngine.EngineName => CreateCartesian3DEngine(descriptor),
            _ => throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for unknown engine '{descriptor.EngineName}'.")
        };
    }

    private static bool TryValidateEngine(SpatialCollectionDescriptor descriptor, ISpatialEngine engine, GeographicDistanceMode? requestedMode)
    {
        if (engine == null)
        {
            return false;
        }

        if (!string.Equals(descriptor.EngineName, engine.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (descriptor.Dimensions != engine.Dimensions)
        {
            return false;
        }

        if (engine is GeographicEngine geographic)
        {
            var configuredMode = descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine;
            if (requestedMode.HasValue && descriptor.Settings.DistanceMode.HasValue && descriptor.Settings.DistanceMode.Value != requestedMode.Value)
            {
                throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Settings.DistanceMode.Value} distances but '{requestedMode.Value}' was requested.");
            }

            var effective = requestedMode ?? configuredMode;
            if (geographic.DistanceMode != effective)
            {
                return false;
            }
        }

        return true;
    }

    private static GeographicEngine CreateGeographicEngine(SpatialCollectionDescriptor descriptor, GeographicDistanceMode? requestedMode)
    {
        var configuredMode = descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine;
        if (requestedMode.HasValue && descriptor.Settings.DistanceMode.HasValue && descriptor.Settings.DistanceMode.Value != requestedMode.Value)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Settings.DistanceMode.Value} distances but '{requestedMode.Value}' was requested.");
        }

        var effectiveMode = requestedMode ?? configuredMode;
        return new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options, effectiveMode);
    }

    private static Cartesian2DEngine CreateCartesian2DEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.Settings.Domain is not BoundingBox domain)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Configure it using Spatial.UseCartesian2D before ensuring indexes.");
        }

        return new Cartesian2DEngine(descriptor.GeometryFieldName, domain, descriptor.Options);
    }

    private static Cartesian3DEngine CreateCartesian3DEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor.Settings.Domain is not BoundingBox domain)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing domain metadata. Configure it using Spatial.UseCartesian3D before ensuring indexes.");
        }

        return new Cartesian3DEngine(descriptor.GeometryFieldName, domain, descriptor.Options);
    }

    private static void EnsureIndexes(BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection, SpatialCollectionDescriptor descriptor)
    {
        var options = descriptor.Options;
        collection.EnsureIndex(options.IndexFieldName);
        collection.EnsureIndex(BaseLiteDB.BsonExpression.Create($"$.{options.IndexFieldName}"));
        collection.EnsureIndex(options.BoundingBoxFieldName);
    }
}
