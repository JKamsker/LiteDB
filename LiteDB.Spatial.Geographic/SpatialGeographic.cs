extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides convenience helpers for configuring and querying geographic spatial indexes.
/// </summary>
public static class SpatialGeographic
{
    public static SpatialCollectionDescriptor EnsurePointIndex(
        SpatialMetadataStore metadata,
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        GeographicDistanceMode distanceMode = GeographicDistanceMode.Haversine)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        var effectiveOptions = options ?? new SpatialIndexOptions();
        var settings = SpatialEngineSettings.ForGeographic(distanceMode);
        var descriptor = new SpatialCollectionDescriptor(collection.Name, GeographicEngine.EngineNameValue, 2, geometryFieldName, effectiveOptions, settings);
        metadata.SaveDescriptor(collection.Name, descriptor);

        collection.EnsureIndex(effectiveOptions.IndexFieldName);
        collection.EnsureIndex(effectiveOptions.BoundingBoxFieldName);

        var engine = new GeographicEngine(geometryFieldName, effectiveOptions, distanceMode);
        var descriptorWithEngine = descriptor.WithEngine(engine);

        SpatialBackfill.Run(collection, descriptorWithEngine, engine);

        return descriptorWithEngine;
    }

    public static ISpatialQueryPlan Near(
        SpatialCollectionDescriptor descriptor,
        GeoPoint center,
        double radius,
        GeographicDistanceMode? distanceMode = null)
    {
        var engine = EnsureEngine(descriptor, distanceMode);
        return engine.PlanNear(center, radius);
    }

    public static ISpatialQueryPlan WithinBoundingBox(
        SpatialCollectionDescriptor descriptor,
        BoundingBox bounds)
    {
        var engine = EnsureEngine(descriptor, null);
        return engine.PlanWithin(bounds);
    }

    private static GeographicEngine EnsureEngine(SpatialCollectionDescriptor descriptor, GeographicDistanceMode? requestedMode)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!string.Equals(descriptor.EngineName, GeographicEngine.EngineNameValue, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for engine '{descriptor.EngineName}' but a geographic engine is required.");
        }

        if (descriptor.Dimensions != 2)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' must be two-dimensional for geographic queries.");
        }

        var configuredMode = descriptor.Settings.DistanceMode ?? GeographicDistanceMode.Haversine;

        if (requestedMode.HasValue && descriptor.Settings.DistanceMode.HasValue && descriptor.Settings.DistanceMode.Value != requestedMode.Value)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for {descriptor.Settings.DistanceMode.Value} distances but '{requestedMode.Value}' was requested.");
        }

        var effectiveMode = requestedMode ?? configuredMode;

        if (descriptor.TryGetEngine(out var runtimeEngine))
        {
            if (runtimeEngine is not GeographicEngine geographic)
            {
                throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for engine '{descriptor.EngineName}' but a geographic engine is required.");
            }

            if (geographic.DistanceMode == effectiveMode)
            {
                return geographic;
            }
        }

        return new GeographicEngine(descriptor.GeometryFieldName, descriptor.Options, effectiveMode);
    }
}
