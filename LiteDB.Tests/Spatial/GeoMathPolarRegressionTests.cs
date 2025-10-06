using System.Collections.Generic;
using FluentAssertions;
using GeographicLib;
using LiteDB.Spatial;
using Xunit;

namespace LiteDB.Tests.Spatial
{
    public class GeoMathPolarRegressionTests
    {
        public static IEnumerable<object[]> HighLatitudeCircleCases => new[]
        {
            new object[] { new GeoPoint(89.0, 0.0), 100_000.0 },
            new object[] { new GeoPoint(-89.0, 30.0), 100_000.0 },
            new object[] { new GeoPoint(70.0, 10.0), 200_000.0 },
            new object[] { new GeoPoint(-70.0, -120.0), 200_000.0 },
            new object[] { new GeoPoint(89.8, 0.0), 50_000.0 },
            new object[] { new GeoPoint(-89.8, 0.0), 50_000.0 }
        };

        public static IEnumerable<object[]> PoleTouchingCircleCases => new[]
        {
            new object[] { new GeoPoint(89.8, 0.0), 50_000.0 },
            new object[] { new GeoPoint(-89.8, 0.0), 50_000.0 }
        };

        [Theory]
        [MemberData(nameof(HighLatitudeCircleCases))]
        public void BoundingBoxForCircle_ShouldContainGeographicLibSamples(GeoPoint center, double radius)
        {
            var bbox = GeoMath.BoundingBoxForCircle(center, radius);

            for (var azimuth = 0; azimuth < 360; azimuth += 2)
            {
                Geodesic.WGS84.Direct(center.Lat, center.Lon, azimuth, radius, out var pointLat, out var pointLon);

                GeoTestHelpers.ContainsWrapAware(bbox, pointLat, pointLon)
                    .Should()
                    .BeTrue(
                        $"Boundary point at azimuth {azimuth}\u00B0 ({pointLat:F6},{GeoTestHelpers.NormalizeLon(pointLon):F6}) lies outside bbox " +
                        $"[{bbox.MinLat:F6},{GeoTestHelpers.NormalizeLon(bbox.MinLon):F6}]..[{bbox.MaxLat:F6},{GeoTestHelpers.NormalizeLon(bbox.MaxLon):F6}] (GeographicLib circle)");
            }
        }

        [Theory]
        [MemberData(nameof(PoleTouchingCircleCases))]
        public void BoundingBoxForCircle_TouchingPole_ShouldSpanAllLongitudes(GeoPoint center, double radius)
        {
            var bbox = GeoMath.BoundingBoxForCircle(center, radius);

            GeoTestHelpers.NormalizeLon(bbox.MinLon)
                .Should()
                .BeApproximately(-180.0, 1e-6, "Circles touching a pole must span all longitudes (GeographicLib circle)");

            GeoTestHelpers.NormalizeLon(bbox.MaxLon)
                .Should()
                .BeApproximately(180.0, 1e-6, "Circles touching a pole must span all longitudes (GeographicLib circle)");
        }

        [Theory]
        [InlineData(89.5, 0.0, 89.5, 180.0)]
        [InlineData(-89.5, 0.0, -89.5, 180.0)]
        public void HaversineNearPoles_ShouldMatchGeographicLib(double lat1, double lon1, double lat2, double lon2)
        {
            Geodesic.WGS84.Inverse(lat1, lon1, lat2, lon2, out var expected);
            var a = new GeoPoint(lat1, lon1);
            var b = new GeoPoint(lat2, lon2);

            var actual = GeoMath.DistanceMeters(a, b, DistanceFormula.Haversine);
            var vincenty = GeoMath.DistanceMeters(a, b, DistanceFormula.Vincenty);

            actual.Should()
                .BeApproximately(expected, 1.0, $"Haversine near pole diverges: expected ~{expected:F3} m (GeographicLib), actual {actual:F3} m");

            vincenty.Should()
                .BeApproximately(expected, 5.0, $"Vincenty control should remain close to GeographicLib near poles; expected {expected:F3} m, actual {vincenty:F3} m");
        }
    }
}
