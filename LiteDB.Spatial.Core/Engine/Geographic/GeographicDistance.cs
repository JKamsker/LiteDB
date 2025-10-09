#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Computes distances between geographic coordinates.
/// </summary>
public sealed class GeographicDistance : ISpatialDistance
{
    private const double EarthRadiusMeters = 6_378_137d;
    private const double Flattening = 1d / 298.257223563d;
    private const double EarthSemiMinorAxis = EarthRadiusMeters * (1d - Flattening);
    private readonly GeographicDistanceMode _mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicDistance"/> class.
    /// </summary>
    /// <param name="mode">The distance calculation mode.</param>
    public GeographicDistance(GeographicDistanceMode mode)
    {
        _mode = mode;
    }

    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        return _mode switch
        {
            GeographicDistanceMode.Haversine => DistanceHaversine(left, right),
            GeographicDistanceMode.Vincenty => DistanceVincenty(left, right),
            _ => DistanceHaversine(left, right)
        };
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        var dz = right.Z - left.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double DistanceHaversine(GeoPoint left, GeoPoint right)
    {
        var lat1 = DegreesToRadians(left.Latitude);
        var lat2 = DegreesToRadians(right.Latitude);
        var dLat = DegreesToRadians(right.Latitude - left.Latitude);
        var dLon = DegreesToRadians(right.Longitude - left.Longitude);

        var a = Math.Pow(Math.Sin(dLat / 2d), 2d) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2d), 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DistanceVincenty(GeoPoint left, GeoPoint right)
    {
        var lat1 = DegreesToRadians(left.Latitude);
        var lat2 = DegreesToRadians(right.Latitude);
        var lon1 = DegreesToRadians(left.Longitude);
        var lon2 = DegreesToRadians(right.Longitude);

        var u1 = Math.Atan((1d - Flattening) * Math.Tan(lat1));
        var u2 = Math.Atan((1d - Flattening) * Math.Tan(lat2));
        var sinU1 = Math.Sin(u1);
        var cosU1 = Math.Cos(u1);
        var sinU2 = Math.Sin(u2);
        var cosU2 = Math.Cos(u2);

        var lambda = lon2 - lon1;
        var previousLambda = double.NaN;
        var iterationLimit = 100;

        var sinSigma = 0d;
        var cosSigma = 0d;
        var sigma = 0d;
        var sinAlpha = 0d;
        var cosSqAlpha = 0d;
        var cos2SigmaM = 0d;

        while (iterationLimit-- > 0)
        {
            var sinLambda = Math.Sin(lambda);
            var cosLambda = Math.Cos(lambda);
            sinSigma = Math.Sqrt(Math.Pow(cosU2 * sinLambda, 2d) + Math.Pow(cosU1 * sinU2 - sinU1 * cosU2 * cosLambda, 2d));

            if (sinSigma == 0d)
            {
                return 0d;
            }

            cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
            sigma = Math.Atan2(sinSigma, cosSigma);
            sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
            cosSqAlpha = 1d - (sinAlpha * sinAlpha);

            cos2SigmaM = cosSqAlpha == 0d ? 0d : cosSigma - 2d * sinU1 * sinU2 / cosSqAlpha;
            var c = Flattening / 16d * cosSqAlpha * (4d + Flattening * (4d - 3d * cosSqAlpha));
            previousLambda = lambda;
            lambda = (lon2 - lon1) + (1d - c) * Flattening * sinAlpha *
                (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1d + 2d * Math.Pow(cos2SigmaM, 2d))));

            if (Math.Abs(lambda - previousLambda) < 1e-12)
            {
                break;
            }
        }

        if (Math.Abs(lambda - previousLambda) >= 1e-9)
        {
            return DistanceHaversine(left, right);
        }

        var uSq = cosSqAlpha * (Math.Pow(EarthRadiusMeters, 2d) - Math.Pow(EarthSemiMinorAxis, 2d)) /
                  Math.Pow(EarthSemiMinorAxis, 2d);
        var a = 1d + uSq / 16384d * (4096d + uSq * (-768d + uSq * (320d - 175d * uSq)));
        var b = uSq / 1024d * (256d + uSq * (-128d + uSq * (74d - 47d * uSq)));
        var deltaSigma = b * sinSigma * (cos2SigmaM + b / 4d * (cosSigma * (-1d + 2d * Math.Pow(cos2SigmaM, 2d)) -
            b / 6d * cos2SigmaM * (-3d + 4d * Math.Pow(sinSigma, 2d)) * (-3d + 4d * Math.Pow(cos2SigmaM, 2d))));
        var distance = EarthSemiMinorAxis * a * (sigma - deltaSigma);
        return distance;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }
}
