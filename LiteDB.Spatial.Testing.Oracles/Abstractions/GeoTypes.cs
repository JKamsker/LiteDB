using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial.Testing.Oracles.Abstractions;

/// <summary>
/// Common coordinate types used across the testing oracles.
/// </summary>
public readonly record struct GeoCoordinate(double Latitude, double Longitude)
{
    public override string ToString() => FormattableString.Invariant($"({Latitude:F6}, {Longitude:F6})");
}

public readonly record struct GeoPoint2D(double X, double Y)
{
    public override string ToString() => FormattableString.Invariant($"({X:F6}, {Y:F6})");
}

public readonly record struct GeoPoint3D(double X, double Y, double Z)
{
    public override string ToString() => FormattableString.Invariant($"({X:F6}, {Y:F6}, {Z:F6})");
}

public static class GeoPointExtensions
{
    public static IReadOnlyList<GeoPoint2D> CloseRing(this IEnumerable<GeoPoint2D> points)
    {
        var materialized = points.ToList();
        if (materialized.Count == 0)
        {
            return materialized;
        }

        if (materialized[0] != materialized[^1])
        {
            materialized.Add(materialized[0]);
        }

        return materialized;
    }
}
