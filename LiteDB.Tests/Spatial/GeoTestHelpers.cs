using System;
using LiteDB.Spatial;

namespace LiteDB.Tests.Spatial
{
    internal static class GeoTestHelpers
    {
        private const double Epsilon = 1e-12;

        public static double NormalizeLon(double lon)
        {
            if (double.IsNaN(lon))
            {
                return lon;
            }

            var result = lon % 360d;

            if (result < -180d)
            {
                result += 360d;
            }
            else if (result >= 180d)
            {
                result -= 360d;
            }

            return result;
        }

        public static bool ContainsWrapAware(GeoBoundingBox boundingBox, double lat, double lon)
        {
            var normalizedLon = NormalizeLon(lon);
            var minLon = NormalizeLon(boundingBox.MinLon);
            var maxLon = NormalizeLon(boundingBox.MaxLon);

            if (lat < boundingBox.MinLat - Epsilon || lat > boundingBox.MaxLat + Epsilon)
            {
                return false;
            }

            if (minLon <= maxLon)
            {
                return normalizedLon >= minLon - Epsilon && normalizedLon <= maxLon + Epsilon;
            }

            return normalizedLon >= minLon - Epsilon || normalizedLon <= maxLon + Epsilon;
        }
    }
}
