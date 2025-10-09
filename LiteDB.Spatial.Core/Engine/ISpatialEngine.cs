namespace LiteDB.Spatial;

/// <summary>
/// Represents a spatial engine capable of producing index-aware query plans and mapping values.
/// </summary>
public interface ISpatialEngine
{
    /// <summary>
    /// Gets the human-readable engine name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the number of spatial dimensions supported by the engine.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Gets the options associated with the engine.
    /// </summary>
    SpatialIndexOptions Options { get; }

    /// <summary>
    /// Gets the encoder used to map coordinates into index keys.
    /// </summary>
    ISpatialIndexEncoder IndexEncoder { get; }

    /// <summary>
    /// Gets the component responsible for mapping values to index fields.
    /// </summary>
    ISpatialMapper Mapper { get; }

    /// <summary>
    /// Gets the distance calculator used for exact filtering.
    /// </summary>
    ISpatialDistance Distance { get; }

    /// <summary>
    /// Creates a query plan that selects values within the provided radius of the target point.
    /// </summary>
    ISpatialQueryPlan PlanNear(global::LiteDB.Spatial.GeoPoint center, double radius);

    /// <summary>
    /// Creates a query plan that selects values within the provided radius of the target point.
    /// </summary>
    ISpatialQueryPlan PlanNear(global::LiteDB.Spatial.GeoPoint3D center, double radius);

    /// <summary>
    /// Creates a query plan that selects values within the provided bounding box.
    /// </summary>
    ISpatialQueryPlan PlanWithin(BoundingBox bounds);
}
