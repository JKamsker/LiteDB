extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Benchmarks.Models.Spatial
{
    internal static class SpatialDocumentGenerator
    {
        private const int Seed = 1337;

        public static List<SpatialDocument> Generate(int count)
        {
            var random = new Random(Seed);
            var documents = new List<SpatialDocument>(count);

            for (var i = 0; i < count; i++)
            {
                var latitude = random.NextDouble() * 0.8 - 0.4;
                var longitude = random.NextDouble() * 0.8 - 0.4;
                var location = new GeoPoint(longitude, latitude);

                var region = BuildSquare(location, random.NextDouble() * 0.05 + 0.01);
                var route = BuildRoute(location, random);

                documents.Add(new SpatialDocument
                {
                    Id = i + 1,
                    Name = $"Place #{i + 1}",
                    Location = location,
                    Region = region,
                    Route = route
                });
            }

            return documents;
        }

        public static List<BaseLiteDB.BsonDocument> GenerateGeographicDocuments(int count)
        {
            var random = new Random(Seed);
            var documents = new List<BaseLiteDB.BsonDocument>(count);

            for (var i = 0; i < count; i++)
            {
                var latitude = random.NextDouble() * 0.8 - 0.4;
                var longitude = random.NextDouble() * 0.8 - 0.4;

                var document = new BaseLiteDB.BsonDocument
                {
                    ["_id"] = i + 1,
                    ["name"] = $"Place #{i + 1}",
                    ["location"] = new BaseLiteDB.BsonDocument
                    {
                        ["longitude"] = longitude,
                        ["latitude"] = latitude
                    }
                };

                documents.Add(document);
            }

            return documents;
        }

        public static GeoPolygon BuildSearchPolygon(double centerLat, double centerLon, double radiusDegrees)
        {
            var center = new GeoPoint(centerLon, centerLat);
            return BuildSquare(center, radiusDegrees);
        }

        private static GeoPolygon BuildSquare(GeoPoint center, double halfExtent)
        {
            var minLat = ClampLatitude(center.Latitude - halfExtent);
            var maxLat = ClampLatitude(center.Latitude + halfExtent);
            var minLon = NormalizeLongitude(center.Longitude - halfExtent);
            var maxLon = NormalizeLongitude(center.Longitude + halfExtent);

            var points = new List<GeoPoint>
            {
                new GeoPoint(minLon, maxLat),
                new GeoPoint(maxLon, maxLat),
                new GeoPoint(maxLon, minLat),
                new GeoPoint(minLon, minLat),
                new GeoPoint(minLon, maxLat)
            };

            return new GeoPolygon(points);
        }

        private static GeoLineString BuildRoute(GeoPoint start, Random random)
        {
            var midLat = start.Latitude + random.NextDouble() * 0.1 - 0.05;
            var midLon = start.Longitude + random.NextDouble() * 0.1 - 0.05;
            var endLat = start.Latitude + random.NextDouble() * 0.2 - 0.1;
            var endLon = start.Longitude + random.NextDouble() * 0.2 - 0.1;

            var points = new List<GeoPoint>
            {
                start,
                new GeoPoint(midLon, midLat),
                new GeoPoint(endLon, endLat)
            };

            return new GeoLineString(points);
        }

        private static double ClampLatitude(double latitude)
        {
            return Math.Max(-90d, Math.Min(90d, latitude));
        }

        private static double NormalizeLongitude(double lon)
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
    }
}
