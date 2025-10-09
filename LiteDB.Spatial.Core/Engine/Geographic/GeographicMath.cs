#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helpers for geographic calculations that work in degrees.
/// </summary>
internal static class GeographicMath
{
    /// <summary>
    /// Average earth radius expressed in meters (spherical model).
    /// </summary>
    public const double EarthRadiusMeters = 6_371_000d;

    private const double DegToRad = Math.PI / 180d;
    private const double RadToDeg = 180d / Math.PI;

    private const double Wgs84EquatorialRadius = 6_378_137d;
    private const double Wgs84Flattening = 1d / 298.257223563d;
    private const double Wgs84PolarRadius = Wgs84EquatorialRadius * (1d - Wgs84Flattening);

    public static double ToRadians(double degrees)
    {
        return degrees * DegToRad;
    }

    public static double ToDegrees(double radians)
    {
        return radians * RadToDeg;
    }

    public static double NormalizeLongitude(double longitude)
    {
        if (double.IsNaN(longitude))
        {
            return longitude;
        }

        var wrapped = longitude % 360d;
        if (wrapped <= -180d)
        {
            wrapped += 360d;
        }
        else if (wrapped > 180d)
        {
            wrapped -= 360d;
        }

        return wrapped;
    }

    public static double ClampLatitude(double latitude)
    {
        if (double.IsNaN(latitude))
        {
            return latitude;
        }

        if (latitude < -90d)
        {
            return -90d;
        }

        if (latitude > 90d)
        {
            return 90d;
        }

        return latitude;
    }

    public static double NormalizeLongitudeToUnit(double longitude)
    {
        var wrapped = NormalizeLongitude(longitude);
        return (wrapped + 180d) / 360d;
    }

    public static double NormalizeLatitudeToUnit(double latitude)
    {
        var clamped = ClampLatitude(latitude);
        return (clamped + 90d) / 180d;
    }

    public static IReadOnlyList<(double Min, double Max)> SplitLongitudeRange(double minLongitude, double maxLongitude)
    {
        if (double.IsNaN(minLongitude) || double.IsInfinity(minLongitude))
        {
            throw new ArgumentException("Longitude bounds must be finite numbers.", nameof(minLongitude));
        }

        if (double.IsNaN(maxLongitude) || double.IsInfinity(maxLongitude))
        {
            throw new ArgumentException("Longitude bounds must be finite numbers.", nameof(maxLongitude));
        }

        if (maxLongitude - minLongitude >= 360d)
        {
            return new[] { (-180d, 180d) };
        }

        var normalizedMin = NormalizeLongitude(minLongitude);
        var normalizedMax = NormalizeLongitude(maxLongitude);

        if (minLongitude < -180d && maxLongitude > 180d)
        {
            return new[] { (-180d, 180d) };
        }

        if (normalizedMin <= normalizedMax && maxLongitude <= 180d && minLongitude >= -180d)
        {
            return new[] { (normalizedMin, normalizedMax) };
        }

        if (maxLongitude > 180d && minLongitude <= 180d)
        {
            return new[]
            {
                (NormalizeLongitude(minLongitude), 180d),
                (-180d, NormalizeLongitude(maxLongitude))
            };
        }

        if (minLongitude < -180d && maxLongitude >= -180d)
        {
            return new[]
            {
                (NormalizeLongitude(minLongitude), 180d),
                (-180d, NormalizeLongitude(maxLongitude))
            };
        }

        if (normalizedMin <= normalizedMax)
        {
            return new[] { (normalizedMin, normalizedMax) };
        }

        return new[]
        {
            (normalizedMin, 180d),
            (-180d, normalizedMax)
        };
    }

    public static (double MinLongitude, double MinLatitude, double MaxLongitude, double MaxLatitude) BoundingBoxForCircle(
        GeoPoint center,
        double radiusMeters)
    {
        if (radiusMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Radius must be non-negative.");
        }

        var angularDistance = radiusMeters / EarthRadiusMeters;
        var latitudeRadians = ToRadians(center.Latitude);

        var minLatRadians = latitudeRadians - angularDistance;
        var maxLatRadians = latitudeRadians + angularDistance;

        var minLatitude = ClampLatitude(ToDegrees(minLatRadians));
        var maxLatitude = ClampLatitude(ToDegrees(maxLatRadians));

        double minLongitude;
        double maxLongitude;

        if (minLatitude <= -90d || maxLatitude >= 90d)
        {
            minLongitude = -180d;
            maxLongitude = 180d;
        }
        else
        {
            var deltaLongitude = Math.Asin(Math.Sin(angularDistance) / Math.Cos(latitudeRadians));

            if (double.IsNaN(deltaLongitude))
            {
                minLongitude = -180d;
                maxLongitude = 180d;
            }
            else
            {
                var longitudeRadians = ToRadians(center.Longitude);
                minLongitude = ToDegrees(longitudeRadians - deltaLongitude);
                maxLongitude = ToDegrees(longitudeRadians + deltaLongitude);
            }
        }

        return (minLongitude, minLatitude, maxLongitude, maxLatitude);
    }

    public static double HaversineDistance(GeoPoint left, GeoPoint right)
    {
        var lat1 = ToRadians(left.Latitude);
        var lat2 = ToRadians(right.Latitude);
        var dLat = lat2 - lat1;
        var dLon = ToRadians(NormalizeLongitude(right.Longitude - left.Longitude));

        var sinLat = Math.Sin(dLat / 2d);
        var sinLon = Math.Sin(dLon / 2d);
        var cosLat1 = Math.Cos(lat1);
        var cosLat2 = Math.Cos(lat2);

        var hav = sinLat * sinLat + cosLat1 * cosLat2 * sinLon * sinLon;
        hav = Math.Min(1d, Math.Max(0d, hav));

        var c = 2d * Math.Atan2(Math.Sqrt(hav), Math.Sqrt(Math.Max(0d, 1d - hav)));
        return EarthRadiusMeters * c;
    }

    public static double VincentyDistance(GeoPoint left, GeoPoint right)
    {
        var phi1 = ToRadians(left.Latitude);
        var phi2 = ToRadians(right.Latitude);
        var lambda = ToRadians(NormalizeLongitude(right.Longitude - left.Longitude));

        var f = Wgs84Flattening;
        var aRadius = Wgs84EquatorialRadius;
        var bRadius = Wgs84PolarRadius;

        var tanU1 = (1d - f) * Math.Tan(phi1);
        var cosU1 = 1d / Math.Sqrt(1d + tanU1 * tanU1);
        var sinU1 = tanU1 * cosU1;

        var tanU2 = (1d - f) * Math.Tan(phi2);
        var cosU2 = 1d / Math.Sqrt(1d + tanU2 * tanU2);
        var sinU2 = tanU2 * cosU2;

        var lambdaIter = lambda;
        double lambdaPrev;

        const int maxIterations = 100;
        var iteration = 0;

        double sinSigma;
        double cosSigma;
        double sigma;
        double cosSqAlpha = 0d;
        double cos2SigmaM = 0d;

        do
        {
            var sinLambda = Math.Sin(lambdaIter);
            var cosLambda = Math.Cos(lambdaIter);

            var term1 = cosU2 * sinLambda;
            var term2 = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;

            sinSigma = Math.Sqrt(term1 * term1 + term2 * term2);

            if (sinSigma == 0d)
            {
                return 0d;
            }

            cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
            sigma = Math.Atan2(sinSigma, cosSigma);

            var sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
            cosSqAlpha = 1d - sinAlpha * sinAlpha;

            if (cosSqAlpha != 0d)
            {
                cos2SigmaM = cosSigma - 2d * sinU1 * sinU2 / cosSqAlpha;
            }
            else
            {
                cos2SigmaM = 0d;
            }

            var c = f / 16d * cosSqAlpha * (4d + f * (4d - 3d * cosSqAlpha));
            lambdaPrev = lambdaIter;
            lambdaIter = lambda + (1d - c) * f * sinAlpha * (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM)));

            iteration++;
        }
        while (Math.Abs(lambdaIter - lambdaPrev) > 1e-12 && iteration < maxIterations);

        if (iteration == maxIterations)
        {
            return HaversineDistance(left, right);
        }

        var uSq = cosSqAlpha * (aRadius * aRadius - bRadius * bRadius) / (bRadius * bRadius);
        var bigA = 1d + uSq / 16384d * (4096d + uSq * (-768d + uSq * (320d - 175d * uSq)));
        var bigB = uSq / 1024d * (256d + uSq * (-128d + uSq * (74d - 47d * uSq)));

        var deltaSigma = bigB * sinSigma * (cos2SigmaM + bigB / 4d * (cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM) - bigB / 6d * cos2SigmaM * (-3d + 4d * sinSigma * sinSigma) * (-3d + 4d * cos2SigmaM * cos2SigmaM)));
        var distance = bRadius * bigA * (sigma - deltaSigma);
        return distance;
    }
}

