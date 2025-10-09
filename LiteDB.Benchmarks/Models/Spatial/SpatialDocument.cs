extern alias LiteDbSpatialCore;

using System;

using GeoPoint = LiteDbSpatialCore::LiteDB.Spatial.GeoPoint;

namespace LiteDB.Benchmarks.Models.Spatial
{
    public class SpatialDocument
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPoint Location { get; set; } = new GeoPoint(0, 0);
    }
}
