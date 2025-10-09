#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Convenience helpers for working with the three-dimensional Cartesian engine.
/// </summary>
public static class SpatialCartesian3D
{
    /// <summary>
    /// Creates a 3D Cartesian engine configured for the provided geometry field.
    /// </summary>
    public static Cartesian3DEngine CreateEngine(string geometryFieldName, SpatialIndexOptions? options = null)
    {
        return new Cartesian3DEngine(geometryFieldName, options);
    }

    /// <summary>
    /// Creates a descriptor that associates the 3D Cartesian engine with a collection.
    /// </summary>
    public static SpatialCollectionDescriptor CreateDescriptor(
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null)
    {
        var engine = new Cartesian3DEngine(geometryFieldName, options);
        return new SpatialCollectionDescriptor(collectionName, Cartesian3DEngine.EngineName, engine.Dimensions, geometryFieldName, engine.Options, engine);
    }

    /// <summary>
    /// Creates a near query plan using the provided engine.
    /// </summary>
    public static ISpatialQueryPlan Near(Cartesian3DEngine engine, GeoPoint3D center, double radius)
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
    public static ISpatialQueryPlan WithinBoundingBox(Cartesian3DEngine engine, BoundingBox bounds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        return engine.PlanWithin(bounds);
    }
}
