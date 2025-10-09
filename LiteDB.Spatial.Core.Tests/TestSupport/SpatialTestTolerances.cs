#nullable enable

using System;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

/// <summary>
/// Provides shared numeric tolerances for spatial assertions.
/// </summary>
public static class SpatialTestTolerances
{
    /// <summary>
    /// Computes the tolerance for Cartesian distance comparisons.
    /// </summary>
    /// <param name="scale">The scale of the measurement (e.g., expected distance).</param>
    public static double Cartesian(double scale)
    {
        var magnitude = Math.Abs(scale);
        return (1e-9 * magnitude) + 1e-9;
    }
}
