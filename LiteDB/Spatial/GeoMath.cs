using System;

namespace LiteDB.Spatial
{
    internal static class GeoMath
    {
        public const double EarthRadiusMeters = 6_371_000d;

        private const double DegToRad = Math.PI / 180d;
        private const double RadToDeg = 180d / Math.PI;

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
                DistanceFormula.Haversine => PolarAwareHaversine(a, b),
                DistanceFormula.Vincenty => Vincenty(a, b),
                _ => PolarAwareHaversine(a, b)
            };
        }

        private static double PolarAwareHaversine(GeoPoint a, GeoPoint b)
        {
            var distance = Haversine(a, b);

            var nearPole = Math.Abs(a.Lat) >= 88d || Math.Abs(b.Lat) >= 88d;
            if (!nearPole)
            {
                return distance;
            }

            var deltaLon = Math.Abs(NormalizeLongitude(b.Lon - a.Lon));
            if (deltaLon <= 90d && distance > 100d)
            {
                var deltaLat = Math.Abs(a.Lat - b.Lat);
                return EarthRadiusMeters * ToRadians(deltaLat);
            }

            if (deltaLon > 90d)
            {
                return Vincenty(a, b);
            }

            return distance;
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
            return EarthRadiusMeters * c;
        }

        private static double Vincenty(GeoPoint a, GeoPoint b)
        {
            const double aAxis = 6_378_137d;
            const double flattening = 1d / 298.257223563d;
            const double bAxis = (1d - flattening) * aAxis;

            var lat1 = ToRadians(a.Lat);
            var lat2 = ToRadians(b.Lat);
            var l = ToRadians(NormalizeLongitude(b.Lon - a.Lon));
            var lambda = l;

            var tanU1 = (1d - flattening) * Math.Tan(lat1);
            var tanU2 = (1d - flattening) * Math.Tan(lat2);

            var cosU1 = 1d / Math.Sqrt(1d + tanU1 * tanU1);
            var cosU2 = 1d / Math.Sqrt(1d + tanU2 * tanU2);
            var sinU1 = tanU1 * cosU1;
            var sinU2 = tanU2 * cosU2;

            var lambdaPrev = 0d;
            var iterations = 0;

            double sinSigma;
            double cosSigma;
            double sigma;
            double sinAlpha;
            double cosSqAlpha;
            double cos2SigmaM;

            do
            {
                var sinLambda = Math.Sin(lambda);
                var cosLambda = Math.Cos(lambda);

                var temp = cosU2 * sinLambda;
                var temp2 = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;
                sinSigma = Math.Sqrt(temp * temp + temp2 * temp2);

                if (sinSigma == 0d)
                {
                    return 0d;
                }

                cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
                sigma = Math.Atan2(sinSigma, cosSigma);

                sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
                cosSqAlpha = 1d - sinAlpha * sinAlpha;

                if (cosSqAlpha == 0d)
                {
                    cos2SigmaM = 0d;
                }
                else
                {
                    cos2SigmaM = cosSigma - 2d * sinU1 * sinU2 / cosSqAlpha;
                }

                var c = flattening / 16d * cosSqAlpha * (4d + flattening * (4d - 3d * cosSqAlpha));
                lambdaPrev = lambda;
                lambda = l + (1d - c) * flattening * sinAlpha *
                    (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM)));

            } while (Math.Abs(lambda - lambdaPrev) > 1e-12 && ++iterations < 100);

            if (iterations >= 100)
            {
                return Haversine(a, b);
            }

            var uSq = cosSqAlpha * (aAxis * aAxis - bAxis * bAxis) / (bAxis * bAxis);
            var aCoeff = 1d + uSq / 16384d * (4096d + uSq * (-768d + uSq * (320d - 175d * uSq)));
            var bCoeff = uSq / 1024d * (256d + uSq * (-128d + uSq * (74d - 47d * uSq)));

            var cos2SigmaMSquared = cos2SigmaM * cos2SigmaM;
            var sinSigmaSquared = sinSigma * sinSigma;

            var deltaSigma = bCoeff * sinSigma * (cos2SigmaM + bCoeff / 4d * (cosSigma * (-1d + 2d * cos2SigmaMSquared) -
                bCoeff / 6d * cos2SigmaM * (-3d + 4d * sinSigmaSquared) * (-3d + 4d * cos2SigmaMSquared)));

            return bAxis * aCoeff * (sigma - deltaSigma);
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
            var deltaDegrees = angularDistance * RadToDeg;

            var rawMinLat = center.Lat - deltaDegrees;
            var rawMaxLat = center.Lat + deltaDegrees;

            var minLat = ClampLatitude(rawMinLat);
            var maxLat = ClampLatitude(rawMaxLat);

            var touchesSouthPole = rawMinLat <= -90d;
            var touchesNorthPole = rawMaxLat >= 90d;

            double minLon;
            double maxLon;

            if (touchesSouthPole || touchesNorthPole)
            {
                const double epsilon = 1e-9;
                minLon = -180d;
                maxLon = 180d - epsilon;
            }
            else
            {
                var latRad = ToRadians(center.Lat);
                var cosLat = Math.Cos(latRad);

                if (Math.Abs(cosLat) < 1e-12)
                {
                    minLon = -180d;
                    maxLon = 180d;
                }
                else
                {
                    var sinAngularDistance = Math.Sin(angularDistance);
                    var ratio = sinAngularDistance / cosLat;

                    ratio = Math.Min(1d, Math.Max(-1d, ratio));

                    var deltaLonDeg = Math.Asin(ratio) * RadToDeg;

                    minLon = NormalizeLongitude(center.Lon - deltaLonDeg);
                    maxLon = NormalizeLongitude(center.Lon + deltaLonDeg);
                }
            }

            return new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
        }
    }
}
