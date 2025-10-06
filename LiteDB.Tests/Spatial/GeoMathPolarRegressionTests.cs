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

        [Theory]
        [MemberData(nameof(BoundingBoxCases))]
        public void BoundingBoxForCircle_ShouldContainGeodesicCircleSamples(double lat, double lon, double radius)
        {
            var center = new GeoPoint(lat, lon);
            var bbox = GeoMath.BoundingBoxForCircle(center, radius);

            var geodesic = Geodesic.WGS84;

            for (var azimuth = 0; azimuth < 360; azimuth += 2)
            {
                var boundary = geodesic.Direct(lat, lon, azimuth, radius);
                var plat = boundary.Latitude;
                var plon = boundary.Longitude;

                GeoTestHelpers.ContainsWrapAware(bbox, plat, plon)
                    .Should()
                    .BeTrue(
                        "Boundary point at azimuth {0}° ({1:F6},{2:F6}) lies outside bbox [{3:F6},{4:F6}]..[{5:F6},{6:F6}] (GeographicLib circle)",
                        azimuth,
                        plat,
                        plon,
                        bbox.MinLat,
                        bbox.MinLon,
                        bbox.MaxLat,
                        bbox.MaxLon);
            }
        }

        [Theory]
        [InlineData(89.8, 0.0, 50_000d)]
        [InlineData(-89.8, 0.0, 50_000d)]
        public void BoundingBoxForCircle_ShouldSpanAllLongitudesWhenTouchingPole(double lat, double lon, double radius)
        {
            var center = new GeoPoint(lat, lon);
            var bbox = GeoMath.BoundingBoxForCircle(center, radius);

            var geodesic = Geodesic.WGS84;

            for (var azimuth = 0; azimuth < 360; azimuth += 2)
            {
                var boundary = geodesic.Direct(lat, lon, azimuth, radius);
                var plat = boundary.Latitude;
                var plon = boundary.Longitude;

                GeoTestHelpers.ContainsWrapAware(bbox, plat, plon)
                    .Should()
                    .BeTrue(
                        "Pole-touching boundary point at azimuth {0}° ({1:F6},{2:F6}) lies outside bbox [{3:F6},{4:F6}]..[{5:F6},{6:F6}] (GeographicLib circle)",
                        azimuth,
                        plat,
                        plon,
                        bbox.MinLat,
                        bbox.MinLon,
                        bbox.MaxLat,
                        bbox.MaxLon);
            }

            GeoTestHelpers.NormalizeLon(bbox.MinLon)
                .Should()
                .BeApproximately(-180d, 1e-6d, "Circle that touches a pole should span every longitude (GeographicLib)");

            GeoTestHelpers.NormalizeLon(bbox.MaxLon)
                .Should()
                .BeApproximately(180d, 1e-6d, "Circle that touches a pole should span every longitude (GeographicLib)");
        }

        [Theory]
        [InlineData(89.5, 0.0, 89.5, 180.0)]
        [InlineData(-89.5, 0.0, -89.5, 180.0)]
        public void Haversine_ShouldMatchGeographicLibNearPoles(double lat1, double lon1, double lat2, double lon2)
        {
            var expected = Geodesic.WGS84.Inverse(lat1, lon1, lat2, lon2).Distance;

            var pointA = new GeoPoint(lat1, lon1);
            var pointB = new GeoPoint(lat2, lon2);

            var actual = GeoMath.DistanceMeters(pointA, pointB, DistanceFormula.Haversine);
            actual.Should().BeApproximately(expected, 1d, "Haversine near pole diverges: expected ~{0:F3} m (GeographicLib), actual {1:F3} m", expected, actual);

            var vincenty = GeoMath.DistanceMeters(pointA, pointB, DistanceFormula.Vincenty);
            vincenty.Should().BeApproximately(expected, 5d, "Vincenty control deviates: expected ~{0:F3} m (GeographicLib), actual {1:F3} m", expected, vincenty);
        }
    }
}
