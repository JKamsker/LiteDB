using System;
using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Spatial
{
    public static class SpatialQuery
    {
        private const string MortonField = "$._gh";
        private const string BoundingBoxField = "$._mbb";

        public static BsonExpression Near<T>(BsonExpression field, GeoPoint center, double radiusMeters, LiteCollection<T> collection = null, int? precisionBits = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (center == null) throw new ArgumentNullException(nameof(center));
            if (radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));

            var precision = ResolvePrecision(collection, precisionBits);
            var normalizedCenter = center.Normalize();
            var queryBox = GeoMath.BoundingBoxForCircle(normalizedCenter, radiusMeters);
            queryBox = ExpandBoundingBox(queryBox);

            var predicates = new List<BsonExpression>();

            var bounding = BsonExpression.Create($"SPATIAL_MBB_INTERSECTS({BoundingBoxField}, @0, @1, @2, @3)",
                new BsonValue(queryBox.MinLat),
                new BsonValue(queryBox.MinLon),
                new BsonValue(queryBox.MaxLat),
                new BsonValue(queryBox.MaxLon));
            predicates.Add(bounding);

            var near = BsonExpression.Create($"SPATIAL_NEAR({field.Source}, @0, @1, @2, @3)",
                new BsonValue(normalizedCenter.Lat),
                new BsonValue(normalizedCenter.Lon),
                new BsonValue(radiusMeters),
                new BsonValue(Spatial.Options.Distance.ToString()));
            predicates.Add(near);

            var ranges = SpatialIndexing.CoverBoundingBox(queryBox, precision, Spatial.Options.MaxCoveringCells);

            if (ranges.Count == 1)
            {
                var range = ranges[0];
                predicates.Add(Query.Between(MortonField, new BsonValue(range.Start), new BsonValue(range.End)));
            }
            else if (ranges.Count > 1)
            {
                var buffer = new BsonExpression[ranges.Count];

                for (var i = 0; i < ranges.Count; i++)
                {
                    var range = ranges[i];
                    buffer[i] = Query.Between(MortonField, new BsonValue(range.Start), new BsonValue(range.End));
                }

                predicates.Add(Query.Or(buffer));
            }

            return Query.And(predicates.ToArray());
        }

        public static BsonExpression WithinBoundingBox(BsonExpression field, GeoBoundingBox box)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            var expanded = ExpandBoundingBox(box);
            return BsonExpression.Create($"SPATIAL_WITHIN_BOX({field.Source}, @0, @1, @2, @3)",
                new BsonValue(expanded.MinLat),
                new BsonValue(expanded.MinLon),
                new BsonValue(expanded.MaxLat),
                new BsonValue(expanded.MaxLon));
        }

        public static BsonExpression Within(BsonExpression field, GeoPolygon polygon)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var bson = GeoJson.ToBson(polygon);
            return BsonExpression.Create($"SPATIAL_WITHIN({field.Source}, @0)", bson);
        }

        public static BsonExpression Intersects(BsonExpression field, GeoShape shape)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            var bson = GeoJson.ToBson(shape);
            return BsonExpression.Create($"SPATIAL_INTERSECTS({field.Source}, @0)", bson);
        }

        public static BsonExpression Contains(BsonExpression field, GeoPoint point)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (point == null) throw new ArgumentNullException(nameof(point));

            var bson = GeoJson.ToBson(point);
            return BsonExpression.Create($"SPATIAL_CONTAINS({field.Source}, @0)", bson);
        }

        private static int ResolvePrecision<T>(LiteCollection<T> collection, int? precisionBits)
        {
            if (precisionBits.HasValue)
            {
                return precisionBits.Value;
            }

            if (collection != null)
            {
                return SpatialMetadataStore.GetPointIndexPrecision(collection);
            }

            return Spatial.Options.DefaultIndexPrecisionBits;
        }

        private static GeoBoundingBox ExpandBoundingBox(GeoBoundingBox box)
        {
            var padding = GetTotalBoundingPaddingMeters();
            if (padding <= 0d)
            {
                return box;
            }

            return box.Expand(padding);
        }

        private static double GetTotalBoundingPaddingMeters()
        {
            return Spatial.Options.BoundingBoxPaddingMeters + GetAngularToleranceMeters();
        }

        private static double GetAngularToleranceMeters()
        {
            var toleranceDegrees = Spatial.Options.NumericToleranceDegrees;
            if (toleranceDegrees <= 0d)
            {
                return 0d;
            }

            return GeoMath.EarthRadiusMeters * toleranceDegrees * (Math.PI / 180d);
        }
    }
}
