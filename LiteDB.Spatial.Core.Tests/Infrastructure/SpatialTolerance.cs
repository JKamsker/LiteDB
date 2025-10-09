using System;
using System.Globalization;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests.Infrastructure
{
    public static class SpatialTolerance
    {
        public const double EarthDistanceMeters = 0.75d;
        public const double CartesianDistance = 1e-4;

        public static void AssertWithinEarthDistance(string fixtureId, double expectedMeters, double actualMeters, string measurement)
        {
            AssertWithinTolerance("earth-distance", fixtureId, expectedMeters, actualMeters, EarthDistanceMeters, measurement, "m");
        }

        public static void AssertWithinCartesianDistance(string fixtureId, double expected, double actual, string measurement)
        {
            AssertWithinTolerance("cartesian-distance", fixtureId, expected, actual, CartesianDistance, measurement, "units");
        }

        private static void AssertWithinTolerance(string category, string fixtureId, double expected, double actual, double tolerance, string measurement, string units)
        {
            var delta = Math.Abs(expected - actual);
            if (double.IsNaN(actual) || double.IsInfinity(actual) || delta > tolerance)
            {
                SpatialSnapshotWriter.WriteSnapshot(category, fixtureId, new
                {
                    fixtureId,
                    measurement,
                    expected,
                    actual,
                    delta,
                    tolerance,
                    units
                });

                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Fixture '{0}' {1} delta {2:F6}{6} exceeds tolerance {3:F6}{6}. Expected {4:F6}{6}, Actual {5:F6}{6}.",
                    fixtureId,
                    measurement,
                    delta,
                    tolerance,
                    expected,
                    actual,
                    units);

                throw new XunitException(message);
            }
        }
    }
}
