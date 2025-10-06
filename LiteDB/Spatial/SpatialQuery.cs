using System;
using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Spatial
{
    public static class SpatialQuery
    {
        public static BsonExpression Near<T>(BsonExpression field, GeoPoint center, double radiusMeters, LiteCollection<T> collection = null, int? precisionBits = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (center == null) throw new ArgumentNullException(nameof(center));
            if (radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));

            var precision = ResolvePrecision(collection, precisionBits);
            var normalizedCenter = center.Normalize();
            var searchBox = GeoMath.BoundingBoxForCircle(normalizedCenter, radiusMeters);
            var expansionMeters = Math.Max(0d, Spatial.Options.BoundingBoxPaddingMeters + Spatial.Options.DistanceToleranceMeters);
            var queryBox = expansionMeters > 0d ? searchBox.Expand(expansionMeters) : searchBox;

            var predicates = new List<BsonExpression>();

            var boundingPredicate = SpatialQueryBuilder.BuildBoundingBoxPredicate(queryBox);
            if (boundingPredicate != null)
            {
                predicates.Add(boundingPredicate);
            }

            var centerBson = GeoJson.ToBson(normalizedCenter);
            predicates.Add(BsonExpression.Create($"SPATIAL_NEAR({field.Source}, @0, @1, @2)",
                centerBson,
                new BsonValue(radiusMeters),
                new BsonValue(Spatial.Options.Distance.ToString())));

            var ranges = SpatialIndexing.CoverBoundingBox(queryBox, precision, Spatial.Options.MaxCoveringCells);
            var rangePredicate = SpatialQueryBuilder.BuildRangePredicate(ranges);
            if (rangePredicate != null)
            {
                predicates.Add(rangePredicate);
            }

            return Query.And(predicates.ToArray());
        }

        public static BsonExpression WithinBoundingBox(BsonExpression field, GeoBoundingBox box)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            return BsonExpression.Create($"SPATIAL_WITHIN_BOX({field.Source}, @0, @1, @2, @3)",
                box.MinLat, box.MinLon, box.MaxLat, box.MaxLon);
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
    }
}
