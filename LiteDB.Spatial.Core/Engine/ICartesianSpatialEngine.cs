#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Represents a spatial engine that operates over Cartesian coordinates.
/// </summary>
public interface ICartesianSpatialEngine : ISpatialEngine
{
    /// <summary>
    /// Gets the coordinate domain used to normalize values.
    /// </summary>
    BoundingBox Domain { get; }
}
