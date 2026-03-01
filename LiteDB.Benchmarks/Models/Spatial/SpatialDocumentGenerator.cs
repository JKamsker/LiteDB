extern alias SpatialCore;

using System;
using System.Collections.Generic;
using GeoPoint = SpatialCore::LiteDB.Spatial.GeoPoint;

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
                var lat = random.NextDouble() * 0.8 - 0.4;
                var lon = random.NextDouble() * 0.8 - 0.4;
                var location = new GeoPoint(lon, lat);

                documents.Add(new SpatialDocument
                {
                    Id = i + 1,
                    Name = $"Place #{i + 1}",
                    Location = location
                });
            }

            return documents;
        }
    }
}
