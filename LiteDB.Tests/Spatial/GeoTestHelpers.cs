using LiteDB.Spatial;

namespace LiteDB.Tests.Spatial
{
    internal static class GeoTestHelpers
    {
        private const double Epsilon = 1e-12;

        public static double NormalizeLon(double lon)
        {
            if (double.IsNaN(lon) || double.IsInfinity(lon))
            {
                return lon;
            }

            var result = lon % 360.0;

            if (result < -180.0)
            {
                result += 360.0;
            }
            else if (result >= 180.0)
            {
                result -= 360.0;
            }

            if (result == -180.0)
            {
                return -180.0;
            }

            return result;
        }

        public static bool ContainsWrapAware(GeoBoundingBox bbox, double lat, double lon)
        {
            var normalizedLon = NormalizeLon(lon);
            var minLon = NormalizeLon(bbox.MinLon);
            var maxLon = NormalizeLon(bbox.MaxLon);

            var latWithin = lat >= bbox.MinLat - Epsilon && lat <= bbox.MaxLat + Epsilon;

            bool lonWithin;

            if (minLon <= maxLon)
            {
                lonWithin = normalizedLon >= minLon - Epsilon && normalizedLon <= maxLon + Epsilon;
            }
            else
            {
                lonWithin = normalizedLon >= minLon - Epsilon || normalizedLon <= maxLon + Epsilon;
            }

            return latWithin && lonWithin;
        }
    }
}
