using System;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests.Infrastructure;

internal static class SpatialTolerance
{
    public const double EarthDistanceMeters = 5.0;
    public const double EarthAreaSquareMeters = 25.0;
    public const double CartesianDistance = 1e-9;
    public const double CartesianArea = 1e-4;

    public static double AssertWithin(double expected, double actual, double tolerance, string fixtureId, string units)
    {
        var delta = Math.Abs(expected - actual);
        if (delta > tolerance)
        {
            throw new XunitException($"Fixture '{fixtureId}' exceeded tolerance of {tolerance} {units}. Expected {expected}, actual {actual}, delta {delta}.");
        }

        return delta;
    }
}
