#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Represents a spatial engine that operates over geographic coordinates.
/// </summary>
public interface IGeographicSpatialEngine : ISpatialEngine
{
    /// <summary>
    /// Gets the distance calculation mode applied by the engine.
    /// </summary>
    GeographicDistanceMode DistanceMode { get; }
}
