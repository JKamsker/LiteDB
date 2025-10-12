using System;
using System.Collections.Generic;
using LiteDB.Spatial.Testing.Oracles.Abstractions;

namespace LiteDB.Spatial.Testing.Oracles.Oracles;

/// <summary>
/// Thin placeholder around a PostGIS-backed distance oracle.
/// It is disabled unless explicitly opted-in through SPATIAL_DB_TESTS and SPATIAL_ORACLES.
/// </summary>
public sealed class PostgisOracle : OracleBase, IGeodesicOracle, IGeometryOracle2D
{
    public PostgisOracle()
        : base("postgis", isDatabaseOracle: true)
    {
        if (IsAvailable)
        {
            SkipReason = "PostGIS reference implementation is not bundled with tests";
            IsAvailable = false;
        }
    }

    public double GetDistanceMeters(GeoCoordinate start, GeoCoordinate end)
        => throw new NotSupportedException(SkipReason ?? "PostGIS oracle is not active");

    public double GetAreaSquareMeters(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed = true)
        => throw new NotSupportedException(SkipReason ?? "PostGIS oracle is not active");

    public double GetPerimeterMeters(IEnumerable<IReadOnlyList<GeoPoint2D>> rings, bool areRingsClosed = true)
        => throw new NotSupportedException(SkipReason ?? "PostGIS oracle is not active");
}
