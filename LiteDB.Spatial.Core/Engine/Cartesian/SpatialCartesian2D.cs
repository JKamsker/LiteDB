#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Convenience helpers for working with the two-dimensional Cartesian engine.
/// </summary>
public static class SpatialCartesian2D
{
    /// <summary>
    /// Creates a Cartesian 2D engine configured for the provided geometry field.
    /// </summary>
    public static Cartesian2DEngine CreateEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        return new Cartesian2DEngine(geometryFieldName, options);
    }

    /// <summary>
    /// Creates a descriptor that associates the 2D Cartesian engine with a collection.
    /// </summary>
    public static SpatialCollectionDescriptor CreateDescriptor(
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null)
    {
        var engine = new Cartesian2DEngine(geometryFieldName, options);
        return new SpatialCollectionDescriptor(collectionName, Cartesian2DEngine.EngineName, engine.Dimensions, geometryFieldName, engine.Options, engine);
    }

    /// <summary>
    /// Creates a near query plan using the provided engine.
    /// </summary>
    public static ISpatialQueryPlan Near(Cartesian2DEngine engine, GeoPoint center, double radius)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanNear(center, radius);
    }

    /// <summary>
    /// Creates a bounding box query plan using the provided engine.
    /// </summary>
    public static ISpatialQueryPlan WithinBoundingBox(Cartesian2DEngine engine, BoundingBox bounds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanWithin(bounds);
    }
}
