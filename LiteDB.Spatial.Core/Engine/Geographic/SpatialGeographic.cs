#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Convenience helpers for working with the geographic engine.
/// </summary>
public static class SpatialGeographic
{
    /// <summary>
    /// Creates a geographic engine configured for the provided geometry field.
    /// </summary>
    /// <param name="geometryFieldName">The document field containing point geometries.</param>
    /// <param name="options">Optional spatial index options.</param>
    /// <returns>An initialized <see cref="GeographicEngine"/>.</returns>
    public static GeographicEngine CreateEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        return new GeographicEngine(geometryFieldName, options);
    }

    /// <summary>
    /// Creates a descriptor that associates the geographic engine with the specified collection.
    /// </summary>
    public static SpatialCollectionDescriptor CreateDescriptor(
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null)
    {
        var engine = new GeographicEngine(geometryFieldName, options);
        return new SpatialCollectionDescriptor(collectionName, GeographicEngine.EngineName, engine.Dimensions, geometryFieldName, engine.Options, engine);
    }

    /// <summary>
    /// Creates a near query plan using the geographic engine.
    /// </summary>
    public static ISpatialQueryPlan Near(GeographicEngine engine, GeoPoint center, double radius)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Creates a bounding box query plan using the geographic engine.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(GeographicEngine engine, BoundingBox bounds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanWithin(bounds);
    }
}
