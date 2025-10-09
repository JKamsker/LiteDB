using System;
using GeographicLib;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// GeographicLib-backed reference implementation for geodesic distances.
/// </summary>
public sealed class GeographicLibOracle : IGeodesicOracle
{
    private readonly Geodesic _geodesic = Geodesic.WGS84;
    private readonly bool _enabled;

    public GeographicLibOracle()
    {
        _enabled = OracleEnvironment.Allows("geographiclib");
    }

    public string Name => "GeographicLib";

    public bool IsEnabled => _enabled;

    public double DistanceMeters(GeoCoordinate start, GeoCoordinate end)
    {
        EnsureEnabled();
        var data = _geodesic.Inverse(start.Latitude, start.Longitude, end.Latitude, end.Longitude, GeodesicFlags.Distance);
        return data.Distance;
    }

    private void EnsureEnabled()
    {
        if (!_enabled)
        {
            throw new InvalidOperationException("GeographicLib oracle is disabled by SPATIAL_ORACLES.");
        }
    }
}
