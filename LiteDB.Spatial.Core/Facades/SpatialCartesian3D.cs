extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers for configuring and planning queries against Cartesian 3D collections.
/// </summary>
public static class SpatialCartesian3D
{
    public static SpatialCollectionDescriptor EnsurePointIndex(
        SpatialMetadataStore metadata,
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        string geometryFieldName,
        BoundingBox domain,
        SpatialIndexOptions? options = null)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (domain.Dimensions != 3)
        {
            throw new ArgumentException("Cartesian3D indices require a 3D domain.", nameof(domain));
        }

        var effectiveOptions = options ?? new SpatialIndexOptions();
        var descriptor = new SpatialCollectionDescriptor(collection.Name, Cartesian3DEngine.EngineNameValue, 3, geometryFieldName, effectiveOptions);
        metadata.SaveDescriptor(collection.Name, descriptor);

        collection.EnsureIndex(effectiveOptions.IndexFieldName);
        collection.EnsureIndex(effectiveOptions.BoundingBoxFieldName);

        var engine = new Cartesian3DEngine(geometryFieldName, domain, effectiveOptions);
        return descriptor.WithEngine(engine);
    }

    public static ISpatialQueryPlan Near(
        SpatialCollectionDescriptor descriptor,
        GeoPoint3D center,
        double radius)
    {
        var engine = EnsureEngine(descriptor);
        return engine.PlanNear(center, radius);
    }

    public static ISpatialQueryPlan WithinBoundingBox(
        SpatialCollectionDescriptor descriptor,
        BoundingBox bounds)
    {
        var engine = EnsureEngine(descriptor);
        return engine.PlanWithin(bounds);
    }

    private static Cartesian3DEngine EnsureEngine(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!string.Equals(descriptor.EngineName, Cartesian3DEngine.EngineNameValue, StringComparison.Ordinal))
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is configured for engine '{descriptor.EngineName}' but a Cartesian3D engine is required.");
        }

        if (descriptor.Dimensions != 3)
        {
            throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' must be three-dimensional for Cartesian3D queries.");
        }

        if (descriptor.HasEngine && descriptor.Engine is Cartesian3DEngine cartesian)
        {
            return cartesian;
        }

        throw new SpatialMetadataException($"Collection '{descriptor.CollectionName}' is missing an attached Cartesian3D engine. Call EnsurePointIndex before planning queries.");
    }
}
