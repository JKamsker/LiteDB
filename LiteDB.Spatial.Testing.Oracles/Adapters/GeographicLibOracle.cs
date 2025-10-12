using GeographicLib;
using LiteDB.Spatial.Testing.Oracles.Infrastructure;
using LiteDB.Spatial.Testing.Oracles.Interfaces;

namespace LiteDB.Spatial.Testing.Oracles.Adapters;

/// <summary>
/// Thin wrapper around GeographicLib's WGS84 geodesic calculations.
/// </summary>
public sealed class GeographicLibOracle : IGeodesicOracle
{
    private const string OracleKey = "geographiclib";

    /// <inheritdoc />
    public bool IsAvailable => OracleEnvironment.IsOracleEnabled(OracleKey);

    /// <inheritdoc />
    public double DistanceInMeters(GeodeticCoordinate from, GeodeticCoordinate to)
    {
        OracleEnvironment.EnsureOraclesEnabled(OracleKey);
        var result = Geodesic.WGS84.Inverse(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        return result.Distance;
    }
}
