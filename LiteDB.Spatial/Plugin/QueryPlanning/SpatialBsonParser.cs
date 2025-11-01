extern alias LiteDbBase;

using System;
using LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Plugin.QueryPlanning
{
    internal static class SpatialBsonParser
    {
        public static bool TryGetGeoPoint(BsonValue value, out GeoPoint point)
        {
            if (value == null || value.IsNull)
            {
                point = default;
                return false;
            }

            if (value.RawValue is GeoPoint rawPoint)
            {
                point = rawPoint;
                return true;
            }

            if (value.IsArray)
            {
                var array = value.AsArray;
                if (array.Count == 2 &&
                    array[0].IsNumber &&
                    array[1].IsNumber)
                {
                    point = new GeoPoint(array[0].AsDouble, array[1].AsDouble);
                    return true;
                }
            }

            if (value.IsDocument)
            {
                var doc = value.AsDocument;
                if (doc.TryGetValue("Longitude", out var lon) && doc.TryGetValue("Latitude", out var lat))
                {
                    point = new GeoPoint(lon.AsDouble, lat.AsDouble);
                    return true;
                }

                if (doc.TryGetValue("X", out var x) && doc.TryGetValue("Y", out var y))
                {
                    point = new GeoPoint(x.AsDouble, y.AsDouble);
                    return true;
                }
            }

            point = default;
            return false;
        }

        public static bool TryGetGeoPoint3D(BsonValue value, out GeoPoint3D point)
        {
            if (value == null || value.IsNull)
            {
                point = default;
                return false;
            }

            if (value.RawValue is GeoPoint3D rawPoint)
            {
                point = rawPoint;
                return true;
            }

            if (value.IsArray)
            {
                var array = value.AsArray;
                if (array.Count == 3 &&
                    array[0].IsNumber &&
                    array[1].IsNumber &&
                    array[2].IsNumber)
                {
                    point = new GeoPoint3D(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble);
                    return true;
                }
            }

            if (value.IsDocument)
            {
                var doc = value.AsDocument;
                if (doc.TryGetValue("X", out var x) &&
                    doc.TryGetValue("Y", out var y) &&
                    doc.TryGetValue("Z", out var z))
                {
                    point = new GeoPoint3D(x.AsDouble, y.AsDouble, z.AsDouble);
                    return true;
                }
            }

            point = default;
            return false;
        }

        public static bool TryGetBoundingBox(BsonValue value, out BoundingBox box)
        {
            if (value == null || value.IsNull)
            {
                box = default;
                return false;
            }

            if (value.RawValue is BoundingBox existing)
            {
                box = existing;
                return true;
            }

            if (value.IsArray)
            {
                var array = value.AsArray;
                if (array.Count == 4 || array.Count == 6)
                {
                    var values = new double[array.Count];
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (!array[i].IsNumber)
                        {
                            box = default;
                            return false;
                        }

                        values[i] = array[i].AsDouble;
                    }

                    box = BoundingBox.Create(values);
                    return true;
                }
            }

            box = default;
            return false;
        }

        public static bool TryGetDouble(BsonValue value, out double number)
        {
            if (value == null || value.IsNull)
            {
                number = double.NaN;
                return false;
            }

            if (value.IsNumber)
            {
                number = value.AsDouble;
                return !double.IsNaN(number);
            }

            number = double.NaN;
            return false;
        }

        public static bool TryGetGeographicMode(BsonValue value, out GeographicDistanceMode mode)
        {
            if (value == null || value.IsNull)
            {
                mode = GeographicDistanceMode.Haversine;
                return false;
            }

            if (value.IsString && Enum.TryParse(value.AsString, ignoreCase: true, out mode))
            {
                return true;
            }

            if (value.IsInt32)
            {
                var ordinal = value.AsInt32;
                mode = ordinal switch
                {
                    1 => GeographicDistanceMode.Vincenty,
                    _ => GeographicDistanceMode.Haversine
                };
                return true;
            }

            mode = GeographicDistanceMode.Haversine;
            return false;
        }
    }
}
