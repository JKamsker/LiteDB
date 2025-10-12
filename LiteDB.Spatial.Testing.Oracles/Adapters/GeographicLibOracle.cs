using System;
using System.Reflection;
using LiteDB.Spatial.Testing.Oracles.Abstractions;
using LiteDB.Spatial.Testing.Oracles.Internal;

namespace LiteDB.Spatial.Testing.Oracles.Adapters
{
    public sealed class GeographicLibOracle : IGeodesicOracle
    {
        private readonly Func<GeoCoordinate, GeoCoordinate, double> _distance;

        public GeographicLibOracle()
        {
            _distance = TryCreateNativeDelegate() ?? new Func<GeoCoordinate, GeoCoordinate, double>(HaversineDistance);
        }

        public string Name => OracleNames.GeographicLib;

        public bool IsAvailable => true;

        public bool UsesFallback => ReferenceEquals(_distance, HaversineDistance);

        public double DistanceMeters(GeoCoordinate start, GeoCoordinate end)
        {
            return _distance(start, end);
        }

        private static Func<GeoCoordinate, GeoCoordinate, double>? TryCreateNativeDelegate()
        {
            try
            {
                var geodesicType = Type.GetType("GeographicLib.Geodesic, GeographicLib", throwOnError: false, ignoreCase: false);
                if (geodesicType is null)
                {
                    return null;
                }

                object? geodesicInstance = geodesicType.GetProperty("WGS84", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (geodesicInstance is null)
                {
                    geodesicInstance = geodesicType.GetField("WGS84", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                }

                if (geodesicInstance is null)
                {
                    geodesicInstance = Activator.CreateInstance(geodesicType);
                }

                if (geodesicInstance is null)
                {
                    return null;
                }

                var inverseWithOutputs = geodesicType.GetMethod(
                    "Inverse",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[]
                    {
                        typeof(double),
                        typeof(double),
                        typeof(double),
                        typeof(double),
                        typeof(double).MakeByRefType(),
                        typeof(double).MakeByRefType(),
                        typeof(double).MakeByRefType()
                    },
                    null);

                if (inverseWithOutputs is not null)
                {
                    return (start, end) =>
                    {
                        var args = new object?[]
                        {
                            start.Latitude,
                            start.Longitude,
                            end.Latitude,
                            end.Longitude,
                            0d,
                            0d,
                            0d
                        };

                        inverseWithOutputs.Invoke(geodesicInstance, args);
                        return args[4] is double meters ? meters : double.NaN;
                    };
                }

                var simpleInverse = geodesicType.GetMethod(
                    "Inverse",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(double), typeof(double), typeof(double), typeof(double) },
                    null);

                if (simpleInverse is not null)
                {
                    return (start, end) =>
                    {
                        var result = simpleInverse.Invoke(geodesicInstance, new object?[]
                        {
                            start.Latitude,
                            start.Longitude,
                            end.Latitude,
                            end.Longitude
                        });

                        return result is double meters ? meters : double.NaN;
                    };
                }
            }
            catch
            {
                // Swallow and fall back to the Haversine implementation.
            }

            return null;
        }

        private static double HaversineDistance(GeoCoordinate start, GeoCoordinate end)
        {
            const double EarthRadiusMeters = 6_371_008.8d;

            var lat1 = DegreesToRadians(start.Latitude);
            var lat2 = DegreesToRadians(end.Latitude);
            var deltaLat = DegreesToRadians(end.Latitude - start.Latitude);
            var deltaLon = DegreesToRadians(end.Longitude - start.Longitude);

            var sinLat = Math.Sin(deltaLat / 2d);
            var sinLon = Math.Sin(deltaLon / 2d);

            var a = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
            var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));

            return EarthRadiusMeters * c;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180d);
        }
    }
}
