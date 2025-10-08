using System;

namespace LiteDB.Spatial
{
    internal static class GeoMath
    {
        public const double EarthRadiusMeters = 6_371_000d;

        private const double DegToRad = Math.PI / 180d;
        private const double PoleLongitudePaddingDegrees = 1e-12d;
        private const double AntipodalLongitudeToleranceDegrees = 1e-6d;

        private const double Wgs84SemiMajorAxis = 6_378_137d;
        private const double Wgs84SemiMinorAxis = 6_356_752.314245d;
        private const double Wgs84Flattening = 1d / 298.257223563d;
        private const double Wgs84EccentricitySquared = Wgs84Flattening * (2d - Wgs84Flattening);

        private static readonly double Wgs84PoleMeridianArc = ComputeMeridianArc(Math.PI / 2d);

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
            var distance = HaversineCore(a, b);

            if (Math.Abs(a.Lat) > 89d && Math.Abs(b.Lat) > 89d)
            {
                var lonDifference = Math.Abs(NormalizeLongitude(b.Lon - a.Lon));

                if (Math.Abs(lonDifference - 180d) <= AntipodalLongitudeToleranceDegrees)
                {
                    var vincenty = Vincenty(a, b, allowSphericalFallback: false);

                    if (!double.IsNaN(vincenty))
                    {
                        return vincenty;
                    }

                    var lat1Radians = ToRadians(Math.Abs(a.Lat));
                    var lat2Radians = ToRadians(Math.Abs(b.Lat));
                    var arcToPoleA = Wgs84PoleMeridianArc - ComputeMeridianArc(lat1Radians);
                    var arcToPoleB = Wgs84PoleMeridianArc - ComputeMeridianArc(lat2Radians);

                    return arcToPoleA + arcToPoleB;
                }

                var deltaLat = ToRadians(Math.Abs(a.Lat - b.Lat));
                return EarthRadiusMeters * deltaLat;
            }

            return distance;
        }

        private static double HaversineCore(GeoPoint a, GeoPoint b)
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
            var vincenty = Vincenty(a, b, allowSphericalFallback: true);

            if (double.IsNaN(vincenty))
            {
                return HaversineCore(a, b);
            }

            return vincenty;
        }

        private static double Vincenty(GeoPoint a, GeoPoint b, bool allowSphericalFallback)
        {
            var lat1 = ToRadians(a.Lat);
            var lat2 = ToRadians(b.Lat);
            var lonDiff = ToRadians(NormalizeLongitude(b.Lon - a.Lon));

            var reducedLat1 = Math.Atan((1d - Wgs84Flattening) * Math.Tan(lat1));
            var reducedLat2 = Math.Atan((1d - Wgs84Flattening) * Math.Tan(lat2));

            var sinU1 = Math.Sin(reducedLat1);
            var cosU1 = Math.Cos(reducedLat1);
            var sinU2 = Math.Sin(reducedLat2);
            var cosU2 = Math.Cos(reducedLat2);

            var lambda = lonDiff;
            var previousLambda = 0d;
            var converged = false;

            double sinSigma = 0d;
            double cosSigma = 0d;
            double sigma = 0d;
            double sinAlpha = 0d;
            double cosSqAlpha = 0d;
            double cos2SigmaM = 0d;

            for (var iteration = 0; iteration < 100; iteration++)
            {
                var sinLambda = Math.Sin(lambda);
                var cosLambda = Math.Cos(lambda);

                var term1 = cosU2 * sinLambda;
                var term2 = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;

                sinSigma = Math.Sqrt(term1 * term1 + term2 * term2);

                if (sinSigma == 0d)
                {
                    return 0d;
                }

                cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
                sigma = Math.Atan2(sinSigma, cosSigma);
                sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
                cosSqAlpha = 1d - sinAlpha * sinAlpha;
                cos2SigmaM = cosSqAlpha == 0d ? 0d : cosSigma - 2d * sinU1 * sinU2 / cosSqAlpha;

                var c = Wgs84Flattening / 16d * cosSqAlpha * (4d + Wgs84Flattening * (4d - 3d * cosSqAlpha));
                previousLambda = lambda;
                lambda = lonDiff + (1d - c) * Wgs84Flattening * sinAlpha *
                    (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM)));

                if (Math.Abs(lambda - previousLambda) < 1e-12d)
                {
                    converged = true;
                    break;
                }
            }

            if (!converged)
            {
                return allowSphericalFallback ? HaversineCore(a, b) : double.NaN;
            }

            var aSquared = Wgs84SemiMajorAxis * Wgs84SemiMajorAxis;
            var bSquared = Wgs84SemiMinorAxis * Wgs84SemiMinorAxis;
            var uSquared = cosSqAlpha * (aSquared - bSquared) / bSquared;

            var aCoeff = 1d + uSquared / 16384d * (4096d + uSquared * (-768d + uSquared * (320d - 175d * uSquared)));
            var bCoeff = uSquared / 1024d * (256d + uSquared * (-128d + uSquared * (74d - 47d * uSquared)));

            var deltaSigma = bCoeff * sinSigma * (cos2SigmaM + bCoeff / 4d * (cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM) -
                bCoeff / 6d * cos2SigmaM * (-3d + 4d * sinSigma * sinSigma) * (-3d + 4d * cos2SigmaM * cos2SigmaM)));

            return Wgs84SemiMinorAxis * aCoeff * (sigma - deltaSigma);
        }

        private static double ComputeMeridianArc(double latitudeRadians)
        {
            var e2 = Wgs84EccentricitySquared;
            var e4 = e2 * e2;
            var e6 = e4 * e2;

            return Wgs84SemiMajorAxis * ((1d - e2 / 4d - 3d * e4 / 64d - 5d * e6 / 256d) * latitudeRadians
                - (3d * e2 / 8d + 3d * e4 / 32d + 45d * e6 / 1024d) * Math.Sin(2d * latitudeRadians)
                + (15d * e4 / 256d + 45d * e6 / 1024d) * Math.Sin(4d * latitudeRadians)
                - (35d * e6 / 3072d) * Math.Sin(6d * latitudeRadians));
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

            var minLat = ClampLatitude(center.Lat - angularDistance / DegToRad);
            var maxLat = ClampLatitude(center.Lat + angularDistance / DegToRad);

            var centerLatRadians = ToRadians(center.Lat);
            var reachesNorthPole = centerLatRadians + angularDistance >= Math.PI / 2d;
            var reachesSouthPole = centerLatRadians - angularDistance <= -Math.PI / 2d;

            double minLon;
            double maxLon;

            if (reachesNorthPole || reachesSouthPole)
            {
                minLon = -180d + PoleLongitudePaddingDegrees;
                maxLon = 180d - PoleLongitudePaddingDegrees;
            }
            else
            {
                var sinAngular = Math.Sin(angularDistance);
                var cosLat = Math.Cos(centerLatRadians);
                var ratio = sinAngular / cosLat;
                ratio = Math.Min(1d, Math.Max(-1d, ratio));

                var deltaLon = Math.Asin(ratio) / DegToRad;

                minLon = NormalizeLongitude(center.Lon - deltaLon);
                maxLon = NormalizeLongitude(center.Lon + deltaLon);
            }

            return new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
        }
    }
}
