using System;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

/// <summary>
/// Provides reusable tolerance calculations for spatial comparisons.
/// </summary>
internal static class SpatialTolerance
{
    private const double EarthRelative = 1e-4;
    private const double EarthAbsolute = 0.05;
    private const double CartesianRelative = 1e-9;
    private const double CartesianAbsolute = 1e-9;

    public static double ForEarth(double expectedMeters)
    {
        return Math.Abs(expectedMeters) * EarthRelative + EarthAbsolute;
    }

    public static double ForCartesian(double scale)
    {
        return Math.Abs(scale) * CartesianRelative + CartesianAbsolute;
    }

    public static void AssertEarthDistance(string fixtureId, double expectedMeters, double actualMeters)
    {
        var tolerance = ForEarth(expectedMeters);
        var delta = Math.Abs(actualMeters - expectedMeters);
        if (delta > tolerance)
        {
            FixtureSnapshot.RecordDistanceFailure(fixtureId, expectedMeters, actualMeters, tolerance);
        }

        actualMeters.Should().BeApproximately(expectedMeters, tolerance, $"Fixture '{fixtureId}' exceeded earth tolerance (Δ={delta:F6} m, tol={tolerance:F6} m).");
    }

    public static void AssertCartesianDistance(string fixtureId, double expected, double actual)
    {
        var tolerance = ForCartesian(Math.Max(Math.Abs(expected), Math.Abs(actual)));
        var delta = Math.Abs(actual - expected);
        if (delta > tolerance)
        {
            FixtureSnapshot.RecordDistanceFailure(fixtureId, expected, actual, tolerance);
        }

        actual.Should().BeApproximately(expected, tolerance, $"Fixture '{fixtureId}' exceeded cartesian tolerance (Δ={delta:E6}, tol={tolerance:E6}).");
    }
}
