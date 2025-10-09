using System;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using GeographicLib;

namespace LiteDB.Spatial.Testing.Oracles.Oracles;

public sealed class GeographicLibOracle : OracleBase, IGeodesicOracle
{
    private readonly Geodesic? _geodesic;

    public GeographicLibOracle()
        : base("geographiclib", isDatabaseOracle: false)
    {
        if (!IsAvailable)
        {
            return;
        }

        _geodesic = Geodesic.WGS84;
    }

    public double GetDistanceMeters(GeoCoordinate start, GeoCoordinate end)
    {
        EnsureAvailable();
        var geodesic = _geodesic ?? throw new InvalidOperationException("GeographicLib oracle is not initialised");
        geodesic.Inverse(start.Latitude, start.Longitude, end.Latitude, end.Longitude, out var s12, out _, out _);
        return s12;
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(SkipReason ?? "GeographicLib oracle is disabled");
        }
    }
}
