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

            if (!minLat.IsNumber || !minLon.IsNumber || !maxLat.IsNumber || !maxLon.IsNumber)
            {
                return false;
            }

            var candidate = ToBoundingBox(bboxValue);
            var query = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);
            return candidate.Intersects(query);
        }

        public static BsonValue SPATIAL_NEAR(BsonValue shapeValue, BsonValue centerValue, BsonValue radiusValue)
        {
            return SPATIAL_NEAR(shapeValue, centerValue, radiusValue, BsonValue.Null);
        }

        public static BsonValue SPATIAL_NEAR(BsonValue shapeValue, BsonValue centerValue, BsonValue radiusValue, BsonValue formulaValue)
        {
            if (!radiusValue.IsNumber)
            {
                return false;
            }

            var shape = ToGeoShape(shapeValue) as GeoPoint;
            var center = ToGeoShape(centerValue) as GeoPoint;

            if (shape == null || center == null)
            {
                return false;
            }

            var radius = radiusValue.AsDouble;
            var formula = ParseFormula(formulaValue);
            return SpatialExpressions.Near(shape, center, radius, formula);
        }

        public static BsonValue SPATIAL_WITHIN_BOX(BsonValue shapeValue, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
        {
            if (!minLat.IsNumber || !minLon.IsNumber || !maxLat.IsNumber || !maxLon.IsNumber)
            {
                return false;
            }

            var shape = ToGeoShape(shapeValue);
            if (shape == null)
            {
                return false;
            }

            var box = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);

            return shape switch
            {
                GeoPoint point => box.Contains(point),
                GeoShape geoShape => geoShape.GetBoundingBox().Intersects(box),
                _ => false
            };
        }

        public static BsonValue SPATIAL_WITHIN(BsonValue shapeValue, BsonValue polygonValue)
        {
            var shape = ToGeoShape(shapeValue);
            var polygon = ToGeoShape(polygonValue) as GeoPolygon;

            if (shape == null || polygon == null)
            {
                return false;
            }

            return SpatialExpressions.Within(shape, polygon);
        }

        public static BsonValue SPATIAL_INTERSECTS(BsonValue leftValue, BsonValue rightValue)
        {
            var left = ToGeoShape(leftValue);
            var right = ToGeoShape(rightValue);

            if (left == null || right == null)
            {
                return false;
            }

            return SpatialExpressions.Intersects(left, right);
        }

        public static BsonValue SPATIAL_CONTAINS(BsonValue shapeValue, BsonValue pointValue)
        {
            var shape = ToGeoShape(shapeValue);
            var point = ToGeoShape(pointValue) as GeoPoint;

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

        private static GeoShape ToGeoShape(BsonValue value)
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

            return null;
        }

        private static DistanceFormula ParseFormula(BsonValue formulaValue)
        {
            if (formulaValue == null || formulaValue.IsNull)
            {
                return Spatial.Spatial.Options.Distance;
            }

            if (formulaValue.IsString && Enum.TryParse(formulaValue.AsString, out DistanceFormula parsed))
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
