#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Implements geographic distance calculations for <see cref="GeoPoint"/> values.
/// </summary>
public sealed class GeographicDistance : ISpatialDistance
{
    private const double EarthRadiusMeters = 6378137d;
    private const double Flattening = 1 / 298.257223563d;
    private const double SemiMinorAxis = EarthRadiusMeters * (1 - Flattening);
    private readonly GeographicDistanceMode _mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicDistance"/> class.
    /// </summary>
    /// <param name="mode">The distance algorithm that should be used.</param>
    public GeographicDistance(GeographicDistanceMode mode)
    {
        _mode = mode;
    }

    /// <inheritdoc />
    public double Distance(GeoPoint left, GeoPoint right)
    {
        return _mode switch
        {
            GeographicDistanceMode.Haversine => Haversine(left, right),
            GeographicDistanceMode.Vincenty => Vincenty(left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(_mode))
        };
    }

    /// <inheritdoc />
    public double Distance(GeoPoint3D left, GeoPoint3D right)
    {
        throw new NotSupportedException("Geographic distance only supports two-dimensional points.");
    }

    private static double Haversine(GeoPoint left, GeoPoint right)
    {
        var lat1 = ToRadians(left.Latitude);
        var lat2 = ToRadians(right.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLon = ToRadians(right.Longitude - left.Longitude);

        var sinLat = Math.Sin(deltaLat / 2);
        var sinLon = Math.Sin(deltaLon / 2);

        var a = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private static double Vincenty(GeoPoint left, GeoPoint right)
    {
        var lat1 = ToRadians(left.Latitude);
        var lat2 = ToRadians(right.Latitude);
        var lon1 = ToRadians(left.Longitude);
        var lon2 = ToRadians(right.Longitude);

        var u1 = Math.Atan((1 - Flattening) * Math.Tan(lat1));
        var u2 = Math.Atan((1 - Flattening) * Math.Tan(lat2));
        var sinU1 = Math.Sin(u1);
        var cosU1 = Math.Cos(u1);
        var sinU2 = Math.Sin(u2);
        var cosU2 = Math.Cos(u2);

        var lambda = lon2 - lon1;
        var prevLambda = double.NaN;
        const double epsilon = 1e-12;
        var iteration = 0;

        while (true)
        {
            var sinLambda = Math.Sin(lambda);
            var cosLambda = Math.Cos(lambda);
            var sinSigma = Math.Sqrt(Math.Pow(cosU2 * sinLambda, 2) + Math.Pow(cosU1 * sinU2 - sinU1 * cosU2 * cosLambda, 2));

            if (sinSigma == 0)
            {
                return 0; // coincident points
            }

            var cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
            var sigma = Math.Atan2(sinSigma, cosSigma);
            var sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
            var cosSqAlpha = 1 - sinAlpha * sinAlpha;

            var cos2SigmaM = cosSqAlpha == 0 ? 0 : cosSigma - 2 * sinU1 * sinU2 / cosSqAlpha;
            var c = Flattening / 16 * cosSqAlpha * (4 + Flattening * (4 - 3 * cosSqAlpha));
            prevLambda = lambda;
            lambda = (lon2 - lon1) + (1 - c) * Flattening * sinAlpha * (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));

            iteration++;
            if (Math.Abs(lambda - prevLambda) <= epsilon || iteration > 200)
            {
                break;
            }
        }

        var sinLambdaFinal = Math.Sin(lambda);
        var cosLambdaFinal = Math.Cos(lambda);
        var sinSigmaFinal = Math.Sqrt(Math.Pow(cosU2 * sinLambdaFinal, 2) + Math.Pow(cosU1 * sinU2 - sinU1 * cosU2 * cosLambdaFinal, 2));

        if (sinSigmaFinal == 0)
        {
            return 0;
        }

        var cosSigmaFinal = sinU1 * sinU2 + cosU1 * cosU2 * cosLambdaFinal;
        var sigmaFinal = Math.Atan2(sinSigmaFinal, cosSigmaFinal);
        var sinAlphaFinal = cosU1 * cosU2 * sinLambdaFinal / sinSigmaFinal;
        var cosSqAlphaFinal = 1 - sinAlphaFinal * sinAlphaFinal;
        var cos2SigmaMFinal = cosSqAlphaFinal == 0 ? 0 : cosSigmaFinal - 2 * sinU1 * sinU2 / cosSqAlphaFinal;
        var uSq = cosSqAlphaFinal * (EarthRadiusMeters * EarthRadiusMeters - SemiMinorAxis * SemiMinorAxis) / (SemiMinorAxis * SemiMinorAxis);
        var aCoeff = 1 + uSq / 16384 * (4096 + uSq * (-768 + uSq * (320 - 175 * uSq)));
        var bCoeff = uSq / 1024 * (256 + uSq * (-128 + uSq * (74 - 47 * uSq)));
        var deltaSigma = bCoeff * sinSigmaFinal * (cos2SigmaMFinal + bCoeff / 4 * (cosSigmaFinal * (-1 + 2 * cos2SigmaMFinal * cos2SigmaMFinal) - bCoeff / 6 * cos2SigmaMFinal * (-3 + 4 * sinSigmaFinal * sinSigmaFinal) * (-3 + 4 * cos2SigmaMFinal * cos2SigmaMFinal)));

        return SemiMinorAxis * aCoeff * (sigmaFinal - deltaSigma);
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }
}
