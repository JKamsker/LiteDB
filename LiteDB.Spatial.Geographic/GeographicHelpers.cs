#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

internal static class GeographicHelpers
{
    internal const double EarthRadiusMeters = 6_378_137d;
    internal const double MinLatitude = -90d;
    internal const double MaxLatitude = 90d;

    internal static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    internal static double RadiansToDegrees(double radians) => radians * (180d / Math.PI);

    internal static double NormalizeLongitude(double longitude)
    {
        if (double.IsNaN(longitude) || double.IsInfinity(longitude))
        {
            throw new ArgumentException("Longitude must be a finite number.", nameof(longitude));
        }

        var result = longitude % 360d;

        if (result < -180d)
        {
            result += 360d;
        }
        else if (result > 180d)
        {
            result -= 360d;
        }

        return result;
    }

    internal static double ClampLatitude(double latitude)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude))
        {
            throw new ArgumentException("Latitude must be a finite number.", nameof(latitude));
        }

        if (latitude < MinLatitude)
        {
            return MinLatitude;
        }

        if (latitude > MaxLatitude)
        {
            return MaxLatitude;
        }

        return latitude;
    }

    internal static double LongitudeToUnit(double longitude)
    {
        return (NormalizeLongitude(longitude) + 180d) / 360d;
    }

    internal static double LatitudeToUnit(double latitude)
    {
        return (ClampLatitude(latitude) + 90d) / 180d;
    }

    internal static GeographicBounds CreateBounds(GeoPoint center, double radiusMeters)
    {
        if (double.IsNaN(radiusMeters) || double.IsInfinity(radiusMeters) || radiusMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Radius must be a non-negative finite number.");
        }

        var angularDistance = radiusMeters / EarthRadiusMeters;
        var centerLatRad = DegreesToRadians(center.Latitude);

        var minLat = ClampLatitude(RadiansToDegrees(centerLatRad - angularDistance));
        var maxLat = ClampLatitude(RadiansToDegrees(centerLatRad + angularDistance));

        double deltaLonDegrees;

        if (minLat <= MinLatitude || maxLat >= MaxLatitude)
        {
            deltaLonDegrees = 180d;
        }
        else
        {
            var sinAngular = Math.Sin(angularDistance);
            var cosLat = Math.Cos(centerLatRad);
            var ratio = sinAngular / cosLat;

            if (ratio > 1d)
            {
                ratio = 1d;
            }
            else if (ratio < -1d)
            {
                ratio = -1d;
            }

            deltaLonDegrees = RadiansToDegrees(Math.Asin(ratio));
        }

        var minLon = center.Longitude - deltaLonDegrees;
        var maxLon = center.Longitude + deltaLonDegrees;

        if (deltaLonDegrees >= 180d || maxLon - minLon >= 360d)
        {
            minLon = -180d;
            maxLon = 180d;
        }

        return new GeographicBounds(minLon, maxLon, minLat, maxLat, isNormalized: false);
    }

    internal static GeographicBounds FromBoundingBox(BoundingBox bounds)
    {
        if (bounds.Dimensions != 2)
        {
            throw new ArgumentException("Geographic bounds must be two-dimensional.", nameof(bounds));
        }

        var minLat = ClampLatitude(bounds.MinY);
        var maxLat = ClampLatitude(bounds.MaxY);

        return new GeographicBounds(bounds.MinX, bounds.MaxX, minLat, maxLat, isNormalized: false);
    }
}

internal readonly struct GeographicBounds
{
    public GeographicBounds(double minLongitude, double maxLongitude, double minLatitude, double maxLatitude, bool isNormalized)
    {
        if (double.IsNaN(minLongitude) || double.IsNaN(maxLongitude) || double.IsNaN(minLatitude) || double.IsNaN(maxLatitude))
        {
            throw new ArgumentException("Bounds must be described using finite numbers.");
        }

        if (double.IsInfinity(minLongitude) || double.IsInfinity(maxLongitude) || double.IsInfinity(minLatitude) || double.IsInfinity(maxLatitude))
        {
            throw new ArgumentException("Bounds must be described using finite numbers.");
        }

        if (maxLatitude < minLatitude)
        {
            throw new ArgumentException("Maximum latitude must be greater than or equal to minimum latitude.");
        }

        if (maxLongitude < minLongitude)
        {
            throw new ArgumentException("Maximum longitude must be greater than or equal to minimum longitude.");
        }

        MinLongitude = minLongitude;
        MaxLongitude = maxLongitude;
        MinLatitude = minLatitude;
        MaxLatitude = maxLatitude;
        IsNormalized = isNormalized;
    }

    public double MinLongitude { get; }

    public double MaxLongitude { get; }

    public double MinLatitude { get; }

    public double MaxLatitude { get; }

    public bool IsNormalized { get; }

    public BoundingBox ToBoundingBox()
    {
        return BoundingBox.From2D(MinLongitude, MinLatitude, MaxLongitude, MaxLatitude);
    }

    public IReadOnlyList<GeographicBounds> SplitForCover()
    {
        if (CoversEntireWorld)
        {
            return new[]
            {
                new GeographicBounds(-180d, 180d, MinLatitude, MaxLatitude, isNormalized: true)
            };
        }

        var minWrapped = GeographicHelpers.NormalizeLongitude(MinLongitude);
        var maxWrapped = GeographicHelpers.NormalizeLongitude(MaxLongitude);

        if (maxWrapped >= minWrapped)
        {
            return new[]
            {
                new GeographicBounds(minWrapped, maxWrapped, MinLatitude, MaxLatitude, isNormalized: true)
            };
        }

        return new[]
        {
            new GeographicBounds(minWrapped, 180d, MinLatitude, MaxLatitude, isNormalized: true),
            new GeographicBounds(-180d, maxWrapped, MinLatitude, MaxLatitude, isNormalized: true)
        };
    }

    public BoundingBox ToNormalizedBoundingBox()
    {
        if (!IsNormalized)
        {
            throw new InvalidOperationException("Bounds must be normalized before converting to the unit interval.");
        }

        return BoundingBox.From2D(
            GeographicHelpers.LongitudeToUnit(MinLongitude),
            GeographicHelpers.LatitudeToUnit(MinLatitude),
            GeographicHelpers.LongitudeToUnit(MaxLongitude),
            GeographicHelpers.LatitudeToUnit(MaxLatitude));
    }

    private bool CoversEntireWorld => MaxLongitude - MinLongitude >= 360d || (MinLongitude <= -180d && MaxLongitude >= 180d);
}
