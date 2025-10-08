using System;

namespace LiteDB.Spatial
{
    internal static class GeoMath
    {
        public const double EarthRadiusMeters = 6_371_000d;

        private const double DegToRad = Math.PI / 180d;
        private const double RadToDeg = 180d / Math.PI;
        private const double HalfPi = Math.PI / 2d;

        private const double Wgs84A = 6_378_137d;
        private const double Wgs84F = 1d / 298.257223563d;
        private const double Wgs84B = Wgs84A * (1d - Wgs84F);

        internal static double EpsilonDegrees => Spatial.Options.ToleranceDegrees;

        public static double ClampLatitude(double latitude)
        {
            return Math.Max(-90d, Math.Min(90d, latitude));
        }

        public static double NormalizeLongitude(double lon)
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

        public static double ToRadians(double degrees)
        {
            return degrees * DegToRad;
        }

        public static double DistanceMeters(GeoPoint a, GeoPoint b, DistanceFormula formula = DistanceFormula.Haversine)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return formula switch
            {
                DistanceFormula.Haversine => Haversine(a, b),
                DistanceFormula.Vincenty => Vincenty(a, b),
                _ => Haversine(a, b)
            };
        }

        private static double Haversine(GeoPoint a, GeoPoint b)
        {
            var lat1 = ToRadians(a.Lat);
            var lat2 = ToRadians(b.Lat);
            var dLat = lat2 - lat1;
            var dLon = ToRadians(NormalizeLongitude(b.Lon - a.Lon));

            var sinLat = Math.Sin(dLat / 2d);
            var sinLon = Math.Sin(dLon / 2d);
            var cosLat1 = Math.Cos(lat1);
            var cosLat2 = Math.Cos(lat2);

            var hav = sinLat * sinLat + cosLat1 * cosLat2 * sinLon * sinLon;
            hav = Math.Min(1d, Math.Max(0d, hav));

            var c = 2d * Math.Atan2(Math.Sqrt(hav), Math.Sqrt(Math.Max(0d, 1d - hav)));
            var distance = EarthRadiusMeters * c;

            if (NeedsPolarCorrection(a, b))
            {
                return Vincenty(a, b);
            }

            return distance;
        }

        private static bool NeedsPolarCorrection(GeoPoint a, GeoPoint b)
        {
            var deltaLon = Math.Abs(NormalizeLongitude(b.Lon - a.Lon));

            return Math.Abs(a.Lat) >= 80d && Math.Abs(b.Lat) >= 80d && deltaLon >= 90d;
        }

        private static double Vincenty(GeoPoint a, GeoPoint b)
        {
            var lat1 = ToRadians(a.Lat);
            var lat2 = ToRadians(b.Lat);
            var l = ToRadians(NormalizeLongitude(b.Lon - a.Lon));

            if (Math.Abs(l) < double.Epsilon && Math.Abs(lat1 - lat2) < double.Epsilon)
            {
                return 0d;
            }

            var u1 = Math.Atan((1d - Wgs84F) * Math.Tan(lat1));
            var u2 = Math.Atan((1d - Wgs84F) * Math.Tan(lat2));
            var sinU1 = Math.Sin(u1);
            var cosU1 = Math.Cos(u1);
            var sinU2 = Math.Sin(u2);
            var cosU2 = Math.Cos(u2);

            var lambda = l;
            var iterations = 0;

            double sinSigma = 0d;
            double cosSigma = 0d;
            double sigma = 0d;
            double sinAlpha = 0d;
            double cos2Alpha = 0d;
            double cos2SigmaM = 0d;

            double lambdaPrev;

            do
            {
                var sinLambda = Math.Sin(lambda);
                var cosLambda = Math.Cos(lambda);

                var cosSin = cosU2 * sinLambda;
                var sinDelta = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;

                sinSigma = Math.Sqrt(cosSin * cosSin + sinDelta * sinDelta);

                if (sinSigma == 0d)
                {
                    return 0d;
                }

                cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
                sigma = Math.Atan2(sinSigma, cosSigma);
                sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
                cos2Alpha = 1d - sinAlpha * sinAlpha;

                if (cos2Alpha == 0d)
                {
                    cos2SigmaM = 0d;
                }
                else
                {
                    cos2SigmaM = cosSigma - 2d * sinU1 * sinU2 / cos2Alpha;
                }

                var c = Wgs84F / 16d * cos2Alpha * (4d + Wgs84F * (4d - 3d * cos2Alpha));
                lambdaPrev = lambda;
                lambda = l + (1d - c) * Wgs84F * sinAlpha *
                    (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM)));
            }
            while (Math.Abs(lambda - lambdaPrev) > 1e-12d && ++iterations < 100);

            if (iterations >= 100)
            {
                return Haversine(a, b);
            }

            var uSquared = cos2Alpha * (Wgs84A * Wgs84A - Wgs84B * Wgs84B) / (Wgs84B * Wgs84B);
            var aCoeff = 1d + uSquared / 16384d * (4096d + uSquared * (-768d + uSquared * (320d - 175d * uSquared)));
            var bCoeff = uSquared / 1024d * (256d + uSquared * (-128d + uSquared * (74d - 47d * uSquared)));
            var deltaSigma = bCoeff * sinSigma * (cos2SigmaM + bCoeff / 4d *
                (cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM) - bCoeff / 6d * cos2SigmaM * (-3d + 4d * sinSigma * sinSigma) *
                (-3d + 4d * cos2SigmaM * cos2SigmaM)));

            return Wgs84B * aCoeff * (sigma - deltaSigma);
        }

        internal static GeoBoundingBox BoundingBoxForCircle(GeoPoint center, double radiusMeters)
        {
            if (center == null)
            {
                throw new ArgumentNullException(nameof(center));
            }

            if (radiusMeters < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            }

            var angularDistance = radiusMeters / EarthRadiusMeters;
            var centerLat = ToRadians(center.Lat);
            var centerLon = ToRadians(center.Lon);

            var minLatRad = Math.Max(centerLat - angularDistance, -HalfPi);
            var maxLatRad = Math.Min(centerLat + angularDistance, HalfPi);

            var touchesPole = centerLat + angularDistance >= HalfPi || centerLat - angularDistance <= -HalfPi;

            double minLonDeg;
            double maxLonDeg;

            if (touchesPole)
            {
                minLonDeg = -180d;
                maxLonDeg = 180d - 1e-12d;
            }
            else
            {
                var cosLat = Math.Cos(centerLat);
                var sinAngular = Math.Sin(angularDistance);
                var ratio = sinAngular / cosLat;
                ratio = Math.Max(-1d, Math.Min(1d, ratio));

                var deltaLon = Math.Asin(ratio);

                var minLonRad = centerLon - deltaLon;
                var maxLonRad = centerLon + deltaLon;

                minLonDeg = minLonRad * RadToDeg;
                maxLonDeg = maxLonRad * RadToDeg;
            }

            var minLatDeg = minLatRad * RadToDeg;
            var maxLatDeg = maxLatRad * RadToDeg;

            return new GeoBoundingBox(minLatDeg, minLonDeg, maxLatDeg, maxLonDeg);
        }
    }
}
