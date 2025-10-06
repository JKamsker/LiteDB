using System.Collections.Generic;
using FluentAssertions;
using GeographicLib;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Tests.Spatial
{
    public class GeoMathPolarRegressionTests
    {
        public static IEnumerable<object[]> BoundingBoxCases()
        {
            yield return new object[] { 89.0, 0.0, 100_000d };
            yield return new object[] { -89.0, 30.0, 100_000d };
            yield return new object[] { 70.0, 10.0, 200_000d };
            yield return new object[] { -70.0, -120.0, 200_000d };
        }

        public static IEnumerable<object[]> PoleTouchingCases()
        {
            yield return new object[] { 89.8, 0.0, 50_000d };
            yield return new object[] { -89.8, 0.0, 50_000d };
        }

        [Theory]
        [MemberData(nameof(BoundingBoxCases))]
        public void BoundingBoxForCircle_HighLatitudeSamplesEscapeBoundingBox(double lat, double lon, double radiusMeters)
        {
            var center = new GeoPoint(lat, lon);
            var bbox = GeoMath.BoundingBoxForCircle(center, radiusMeters);

            for (var azimuth = 0; azimuth < 360; azimuth += 2)
            {
                var point = Geodesic.WGS84.Direct(lat, lon, azimuth, radiusMeters);
                var plat = point.Latitude;
                var plon = GeoTestHelpers.NormalizeLon(point.Longitude);

                GeoTestHelpers.ContainsWrapAware(bbox, plat, plon).Should().BeTrue(
                    "Boundary point at azimuth {0}° ({1:F6},{2:F6}) lies outside bbox [{3:F6},{4:F6}]..[{5:F6},{6:F6}] (GeographicLib circle)",
                    azimuth,
                    plat,
                    plon,
                    bbox.MinLat,
                    GeoTestHelpers.NormalizeLon(bbox.MinLon),
                    bbox.MaxLat,
                    GeoTestHelpers.NormalizeLon(bbox.MaxLon));
            }
        }

        [Theory]
        [MemberData(nameof(PoleTouchingCases))]
        public void BoundingBoxForCircle_PoleTouchingCircleDoesNotSpanAllLongitudes(double lat, double lon, double radiusMeters)
        {
            var center = new GeoPoint(lat, lon);
            var bbox = GeoMath.BoundingBoxForCircle(center, radiusMeters);

            var normalizedMinLon = GeoTestHelpers.NormalizeLon(bbox.MinLon);
            var normalizedMaxLon = GeoTestHelpers.NormalizeLon(bbox.MaxLon);

            normalizedMinLon.Should().BeApproximately(-180d, 1e-6,
                "Pole-touching circle should force bbox to span all longitudes; GeographicLib circle reaches the pole");

            normalizedMaxLon.Should().BeApproximately(180d, 1e-6,
                "Pole-touching circle should force bbox to span all longitudes; GeographicLib circle reaches the pole");

            for (var azimuth = 0; azimuth < 360; azimuth += 2)
            {
                var point = Geodesic.WGS84.Direct(lat, lon, azimuth, radiusMeters);
                var plat = point.Latitude;
                var plon = GeoTestHelpers.NormalizeLon(point.Longitude);

                GeoTestHelpers.ContainsWrapAware(bbox, plat, plon).Should().BeTrue(
                    "Pole-touching boundary point at azimuth {0}° ({1:F6},{2:F6}) lies outside bbox [{3:F6},{4:F6}]..[{5:F6},{6:F6}] (GeographicLib circle)",
                    azimuth,
                    plat,
                    plon,
                    bbox.MinLat,
                    GeoTestHelpers.NormalizeLon(bbox.MinLon),
                    bbox.MaxLat,
                    GeoTestHelpers.NormalizeLon(bbox.MaxLon));
            }
        }

        [Theory]
        [InlineData(89.5, 0.0, 89.5, 180.0)]
        [InlineData(-89.5, 0.0, -89.5, 180.0)]
        public void DistanceMeters_HaversineShortcutsAcrossPole(double lat1, double lon1, double lat2, double lon2)
        {
            var expected = Geodesic.WGS84.Inverse(lat1, lon1, lat2, lon2).Distance;

            var pointA = new GeoPoint(lat1, lon1);
            var pointB = new GeoPoint(lat2, lon2);

            var actual = GeoMath.DistanceMeters(pointA, pointB, DistanceFormula.Haversine);
            actual.Should().BeApproximately(expected, 1d,
                "Haversine near pole diverges: expected ~{0:F3} m (GeographicLib), actual {1:F3} m",
                expected,
                actual);

            var vincenty = GeoMath.DistanceMeters(pointA, pointB, DistanceFormula.Vincenty);
            vincenty.Should().BeApproximately(expected, 5d,
                "Vincenty control regression: expected ~{0:F3} m (GeographicLib), actual {1:F3} m",
                expected,
                vincenty);
        }
    }
}
