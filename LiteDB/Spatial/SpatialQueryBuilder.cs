using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Spatial
{
    internal static class SpatialQueryBuilder
    {
        public static BsonExpression BuildRangePredicate(IReadOnlyList<(long Start, long End)> ranges)
        {
            if (ranges == null || ranges.Count == 0)
            {
                return null;
            }

            var expressions = new List<BsonExpression>(ranges.Count);

            foreach (var range in ranges)
            {
                expressions.Add(Query.Between("$._gh", new BsonValue(range.Start), new BsonValue(range.End)));
            }

            return CombineOr(expressions);
        }

        public static BsonExpression BuildBoundingBoxPredicate(GeoBoundingBox box)
        {
            var range = new LongitudeRange(box.MinLon, box.MaxLon);
            var expressions = new List<BsonExpression>();

            foreach (var (start, end) in range.GetSegments())
            {
                var segmentBox = new GeoBoundingBox(box.MinLat, start, box.MaxLat, end);

                expressions.Add(BsonExpression.Create(
                    "SPATIAL_MBB_INTERSECTS($._mbb, @0, @1, @2, @3)",
                    segmentBox.MinLat,
                    segmentBox.MinLon,
                    segmentBox.MaxLat,
                    segmentBox.MaxLon));
            }

            return CombineOr(expressions);
        }

        public static BsonExpression CombineSpatialPredicates(BsonExpression rangeExpression, BsonExpression boundingExpression)
        {
            if (rangeExpression == null)
            {
                return boundingExpression;
            }

            if (boundingExpression == null)
            {
                return rangeExpression;
            }

            var parameters = new BsonDocument();

            if (boundingExpression.Parameters != null)
            {
                foreach (var parameter in boundingExpression.Parameters)
                {
                    parameters[parameter.Key] = parameter.Value;
                }
            }

            var source = $"({rangeExpression.Source}) AND ({boundingExpression.Source})";

            return BsonExpression.Create(source, parameters);
        }

        private static BsonExpression CombineOr(IReadOnlyList<BsonExpression> expressions)
        {
            if (expressions.Count == 0)
            {
                return null;
            }

            if (expressions.Count == 1)
            {
                return expressions[0];
            }

            var buffer = new BsonExpression[expressions.Count];

            for (var i = 0; i < expressions.Count; i++)
            {
                buffer[i] = expressions[i];
            }

            return Query.Or(buffer);
        }
    }
}
