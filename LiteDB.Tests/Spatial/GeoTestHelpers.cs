using LiteDB.Spatial;

namespace LiteDB.Tests.Spatial
{
    internal static class GeoTestHelpers
    {
        private const double ComparisonEpsilon = 1e-12;

        public static double NormalizeLon(double lon)
        {
            if (double.IsNaN(lon))
            {
                return lon;
            }

            var result = lon % 360d;

            if (result <= -180d)
            {
                result += 360d;
            }
            else if (result > 180d)
            {
                result -= 360d;
            }

            return result;
        }

        public static bool ContainsWrapAware(GeoBoundingBox bbox, double lat, double lon)
        {
            if (lat < bbox.MinLat - ComparisonEpsilon || lat > bbox.MaxLat + ComparisonEpsilon)
            {
                return false;
            }

            var normalizedLon = NormalizeLon(lon);
            var minLon = NormalizeLon(bbox.MinLon);
            var maxLon = NormalizeLon(bbox.MaxLon);

            if (minLon <= maxLon + ComparisonEpsilon)
            {
                return normalizedLon >= minLon - ComparisonEpsilon && normalizedLon <= maxLon + ComparisonEpsilon;
            }

            return normalizedLon >= minLon - ComparisonEpsilon || normalizedLon <= maxLon + ComparisonEpsilon;
        }
    }
}
