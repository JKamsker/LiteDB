using System;
using LiteDB.Spatial;

namespace LiteDB
{
    internal partial class BsonExpressionMethods
    {
        public static BsonValue SPATIAL_MBB_INTERSECTS(BsonValue bboxValue, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
        {
            if (!bboxValue.IsArray || bboxValue.AsArray.Count != 4)
            {
                return false;
            }

            var candidate = ToBoundingBox(bboxValue);
            var query = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);
            return candidate.Intersects(query);
        }

        public static BsonValue SPATIAL_INTERSECTS_MBB(BsonValue bboxValue, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
        {
            return SPATIAL_MBB_INTERSECTS(bboxValue, minLat, minLon, maxLat, maxLon);
        }

        public static BsonValue SPATIAL_NEAR(BsonValue shapeValue, BsonValue centerValue, BsonValue radiusValue)
        {
            return SPATIAL_NEAR(shapeValue, centerValue, radiusValue, new BsonValue(Spatial.Spatial.Options.Distance.ToString()));
        }

        public static BsonValue SPATIAL_NEAR(BsonValue shapeValue, BsonValue centerValue, BsonValue radiusValue, BsonValue formulaValue)
        {
            var shape = ToShape(shapeValue) as GeoPoint;
            var center = ToShape(centerValue) as GeoPoint;

            if (shape == null || center == null)
            {
                return false;
            }

            var radius = radiusValue.AsDouble;
            var formula = ParseFormula(formulaValue);
            var distance = GeoMath.DistanceMeters(shape, center, formula);
            return distance <= radius + Spatial.Spatial.Options.DistanceToleranceMeters;
        }

        public static BsonValue SPATIAL_WITHIN_BOX(BsonValue shapeValue, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
        {
            var shape = ToShape(shapeValue);
            if (shape == null)
            {
                return false;
            }

            var box = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);
            var tolerance = Spatial.Spatial.Options.NumericToleranceDegrees;

            return shape switch
            {
                GeoPoint point => box.Contains(point, tolerance),
                GeoShape geoShape => geoShape.GetBoundingBox().Intersects(box),
                _ => false
            };
        }

        public static BsonValue SPATIAL_WITHIN(BsonValue shapeValue, BsonValue polygonValue)
        {
            var shape = ToShape(shapeValue);
            var polygon = ToShape(polygonValue) as GeoPolygon;

            if (shape == null || polygon == null)
            {
                return false;
            }

            return SpatialExpressions.Within(shape, polygon);
        }

        public static BsonValue SPATIAL_INTERSECTS(BsonValue leftValue, BsonValue rightValue)
        {
            var left = ToShape(leftValue);
            var right = ToShape(rightValue);

            if (left == null || right == null)
            {
                return false;
            }

            return SpatialExpressions.Intersects(left, right);
        }

        public static BsonValue SPATIAL_CONTAINS(BsonValue shapeValue, BsonValue pointValue)
        {
            var shape = ToShape(shapeValue);
            var point = ToShape(pointValue) as GeoPoint;

            if (shape == null || point == null)
            {
                return false;
            }

            return SpatialExpressions.Contains(shape, point);
        }

        public static BsonValue SPATIAL_CONTAINS_POINT(BsonValue shapeValue, BsonValue pointValue)
        {
            return SPATIAL_CONTAINS(shapeValue, pointValue);
        }

        private static GeoBoundingBox ToBoundingBox(BsonValue value)
        {
            var array = value.AsArray;
            return new GeoBoundingBox(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble, array[3].AsDouble);
        }

        private static GeoShape ToShape(BsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return null;
            }

            if (value.IsDocument)
            {
                return GeoJson.FromBson(value.AsDocument);
            }

            if (value.IsArray && value.AsArray.Count >= 2)
            {
                var array = value.AsArray;
                var lon = array[0].AsDouble;
                var lat = array[1].AsDouble;
                return new GeoPoint(lat, lon);
            }

            if (value.RawValue is GeoShape shape)
            {
                return shape;
            }

            if (value.RawValue is GeoPoint point)
            {
                return point;
            }

            return null;
        }

        private static DistanceFormula ParseFormula(BsonValue formulaValue)
        {
            if (formulaValue.IsString && Enum.TryParse<DistanceFormula>(formulaValue.AsString, out var parsed))
            {
                return parsed;
            }

            if (formulaValue.IsInt32)
            {
                return (DistanceFormula)formulaValue.AsInt32;
            }

            return Spatial.Spatial.Options.Distance;
        }
    }
}
