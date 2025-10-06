using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial
{
    public readonly struct GeoBoundingBox
    {
        public double MinLat { get; }
        public double MinLon { get; }
        public double MaxLat { get; }
        public double MaxLon { get; }

        public GeoBoundingBox(double minLat, double minLon, double maxLat, double maxLon)
        {
            MinLat = GeoMath.ClampLatitude(Math.Min(minLat, maxLat));
            MaxLat = GeoMath.ClampLatitude(Math.Max(minLat, maxLat));
            MinLon = GeoMath.NormalizeLongitude(minLon);
            MaxLon = GeoMath.NormalizeLongitude(maxLon);
        }

        public static GeoBoundingBox FromPoints(IEnumerable<GeoPoint> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var list = points.ToList();
            if (list.Count == 0)
            {
                throw new ArgumentException("Bounding box requires at least one point", nameof(points));
            }

            var minLat = list.Min(p => p.Lat);
            var maxLat = list.Max(p => p.Lat);
            var lons = list.Select(p => p.Lon).ToList();
            var minLon = lons.Min();
            var maxLon = lons.Max();

            return new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
        }

        public double[] ToArray()
        {
            return new[] { MinLat, MinLon, MaxLat, MaxLon };
        }

        public bool Contains(GeoPoint point)
        {
            return Contains(point, 0d);
        }

        public bool Contains(GeoPoint point, double toleranceDegrees)
        {
            if (point == null)
            {
                return false;
            }

            var minLat = MinLat - toleranceDegrees;
            var maxLat = MaxLat + toleranceDegrees;

            if (point.Lat < minLat || point.Lat > maxLat)
            {
                return false;
            }

            return IsLongitudeWithin(point.Lon, toleranceDegrees);
        }

        public bool Intersects(GeoBoundingBox other)
        {
            return !(other.MinLat > MaxLat || other.MaxLat < MinLat) && LongitudesOverlap(other);
        }

        public GeoBoundingBox Expand(double meters)
        {
            if (meters <= 0d)
            {
                return this;
            }

            var angularDistance = meters / GeoMath.EarthRadiusMeters;
            var deltaDegrees = angularDistance * (180d / Math.PI);

            var minLat = GeoMath.ClampLatitude(MinLat - deltaDegrees);
            var maxLat = GeoMath.ClampLatitude(MaxLat + deltaDegrees);
            var minLon = GeoMath.NormalizeLongitude(MinLon - deltaDegrees);
            var maxLon = GeoMath.NormalizeLongitude(MaxLon + deltaDegrees);

            return new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
        }

        private bool LongitudesOverlap(GeoBoundingBox other)
        {
            var lonRange = new LongitudeRange(MinLon, MaxLon);
            var otherRange = new LongitudeRange(other.MinLon, other.MaxLon);
            return lonRange.Intersects(otherRange);
        }

        private bool IsLongitudeWithin(double lon)
        {
            return IsLongitudeWithin(lon, 0d);
        }

        private bool IsLongitudeWithin(double lon, double toleranceDegrees)
        {
            var lonRange = toleranceDegrees > 0d
                ? new LongitudeRange(MinLon - toleranceDegrees, MaxLon + toleranceDegrees)
                : new LongitudeRange(MinLon, MaxLon);
            return lonRange.Contains(lon);
        }
    }
}
