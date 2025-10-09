extern alias SpatialCore;

using System;
using GeoPoint = SpatialCore::LiteDB.Spatial.GeoPoint;

namespace LiteDB.Benchmarks.Models.Spatial
{
    public class SpatialDocument
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPoint Location { get; set; } = new GeoPoint(0, 0);
    }
}
