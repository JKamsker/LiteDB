extern alias LiteDbSpatialCore;

using System;
using System.Collections.Generic;
using BoundingBox = LiteDbSpatialCore::LiteDB.Spatial.BoundingBox;
using GeoPoint = LiteDbSpatialCore::LiteDB.Spatial.GeoPoint;

namespace LiteDB.Benchmarks.Models.Spatial
{
    internal static class SpatialDocumentGenerator
    {
        public static List<SpatialDocument> Generate(int count)
        {
            var random = new Random(1337);
            var documents = new List<SpatialDocument>(count);

            for (var i = 0; i < count; i++)
            {
                var longitude = random.NextDouble() * 0.8 - 0.4;
                var latitude = random.NextDouble() * 0.8 - 0.4;
                var location = new GeoPoint(longitude, latitude);

                documents.Add(new SpatialDocument
                {
                    Id = i + 1,
                    Name = $"Place #{i + 1}",
                    Location = location
                });
            }

            return documents;
        }

        public static BoundingBox BuildSearchBounds(double centerLongitude, double centerLatitude, double halfExtent)
        {
            var minLon = Math.Max(-180d, centerLongitude - halfExtent);
            var maxLon = Math.Min(180d, centerLongitude + halfExtent);
            var minLat = Math.Max(-90d, centerLatitude - halfExtent);
            var maxLat = Math.Min(90d, centerLatitude + halfExtent);

            return BoundingBox.From2D(minLon, minLat, maxLon, maxLat);
        }
    }
}
