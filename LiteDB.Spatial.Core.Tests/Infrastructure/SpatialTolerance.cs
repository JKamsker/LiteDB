using System;
using LiteDB.Spatial.Testing.Oracles;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

public static class SpatialTolerance
{
    private const double EarthSlope = 1e-4;
    private const double EarthOffset = 0.05;
    private const double CartesianSlope = 1e-9;
    private const double CartesianOffset = 1e-9;

    public static double Earth(double expectedMeters) => Math.Abs(expectedMeters) * EarthSlope + EarthOffset;

    public static double Cartesian(double scale) => Math.Abs(scale) * CartesianSlope + CartesianOffset;

    public static string FormatDelta(string fixtureId, double expected, double actual, double tolerance)
    {
        var delta = Math.Abs(expected - actual);
        return $"Fixture '{fixtureId}' exceeded tolerance. Expected={expected:F6}, Actual={actual:F6}, Δ={delta:F6}, Tol={tolerance:F6}";
    }

    public static string FormatDelta(string fixtureId, Point3D expected, Point3D actual, double tolerance)
    {
        var delta = Math.Sqrt(Math.Pow(expected.X - actual.X, 2) + Math.Pow(expected.Y - actual.Y, 2) + Math.Pow(expected.Z - actual.Z, 2));
        return $"Fixture '{fixtureId}' exceeded tolerance. Expected={expected}, Actual={actual}, Δ={delta:E3}, Tol={tolerance:E3}";
    }
}
