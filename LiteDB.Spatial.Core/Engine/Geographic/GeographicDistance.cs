#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Provides distance calculations for geographic coordinates.
/// </summary>
public sealed class GeographicDistance : ISpatialDistance
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicDistance"/> class.
    /// </summary>
    /// <param name="mode">The distance mode used to calculate the arc length.</param>
    public GeographicDistance(GeographicDistanceMode mode)
    {
        Mode = mode;
    }

    /// <summary>
    /// Gets the configured distance mode.
    /// </summary>
    public GeographicDistanceMode Mode { get; }

    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        return Mode switch
        {
            GeographicDistanceMode.Haversine => GeographicMath.HaversineDistance(left, right),
            GeographicDistanceMode.Vincenty => GeographicMath.VincentyDistance(left, right),
            _ => GeographicMath.HaversineDistance(left, right)
        };
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Geographic distance is defined only for two-dimensional coordinates.");
    }
}

/// <summary>
/// Distance formulas supported by the geographic engine.
/// </summary>
public enum GeographicDistanceMode
{
    /// <summary>
    /// Uses a spherical Haversine approximation.
    /// </summary>
    Haversine,

    /// <summary>
    /// Uses the WGS84 Vincenty iterative solution.
    /// </summary>
    Vincenty
}

