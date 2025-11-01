extern alias LiteDbBase;

#nullable enable

using System;
using System.Collections.Generic;
using LiteDB.Spatial;
using LiteDB.Spatial.Plugin.QueryPlanning;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Plugin.Runtime
{
    internal static class SpatialExpressionFunctions
    {
        private const double LongitudeWrapWidth = 360d;
        private const double Epsilon = 1e-9;

        private static readonly BaseLiteDB.BsonValue False = new BaseLiteDB.BsonValue(false);
        private static readonly BaseLiteDB.BsonValue True = new BaseLiteDB.BsonValue(true);

        public static BaseLiteDB.BsonValue InvokeNear(
            BaseLiteDB.BsonDocument root,
            BaseLiteDB.Collation collation,
            BaseLiteDB.BsonDocument parameters,
            BaseLiteDB.BsonValue candidate,
            BaseLiteDB.BsonValue center,
            BaseLiteDB.BsonValue radius,
            BaseLiteDB.BsonValue mode)
        {
            return Near(candidate, center, radius, mode);
        }

        public static BaseLiteDB.BsonValue Near(BaseLiteDB.BsonValue candidate, BaseLiteDB.BsonValue center, BaseLiteDB.BsonValue radius, BaseLiteDB.BsonValue mode)
        {
            if (!SpatialBsonParser.TryGetDouble(radius, out var radiusValue) || radiusValue < 0d || double.IsInfinity(radiusValue) || double.IsNaN(radiusValue))
            {
                return False;
            }

            if (TryGetGeoPoint3D(candidate, out var candidate3D) && TryGetGeoPoint3D(center, out var center3D))
            {
                var distance = new CartesianDistance(supports3D: true).Distance(candidate3D, center3D);
                return distance <= radiusValue ? True : False;
            }

            if (TryGetGeoPoint(candidate, out var candidatePoint) && TryGetGeoPoint(center, out var centerPoint))
            {
                var distanceMode = ParseDistanceMode(mode);
                var distance = new GeographicDistance(distanceMode).Distance(candidatePoint, centerPoint);
                return distance <= radiusValue ? True : False;
            }

            return False;
        }

        public static BaseLiteDB.BsonValue InvokeWithin(
            BaseLiteDB.BsonDocument root,
            BaseLiteDB.Collation collation,
            BaseLiteDB.BsonDocument parameters,
            BaseLiteDB.BsonValue candidate,
            BaseLiteDB.BsonValue polygon)
        {
            return Within(candidate, polygon);
        }

        public static BaseLiteDB.BsonValue Within(BaseLiteDB.BsonValue candidate, BaseLiteDB.BsonValue polygon)
        {
            var area = ToGeometry(polygon) as GeoPolygon;
            if (area == null)
            {
                return False;
            }

            var geometry = ToGeometry(candidate);
            if (geometry is GeoPoint point2D)
            {
                return GeometryHelpers.ContainsPoint(area, point2D) ? True : False;
            }

            if (geometry is GeoPoint3D point3D)
            {
                return GeometryHelpers.ContainsPoint(area, new GeoPoint(point3D.X, point3D.Y)) ? True : False;
            }

            if (geometry is GeoLineString line)
            {
                return GeometryHelpers.LineWithinPolygon(line, area) ? True : False;
            }

            if (geometry is GeoPolygon polygonCandidate)
            {
                return GeometryHelpers.PolygonWithinPolygon(polygonCandidate, area) ? True : False;
            }

            return False;
        }

        public static BaseLiteDB.BsonValue InvokeIntersects(
            BaseLiteDB.BsonDocument root,
            BaseLiteDB.Collation collation,
            BaseLiteDB.BsonDocument parameters,
            BaseLiteDB.BsonValue left,
            BaseLiteDB.BsonValue right)
        {
            return Intersects(left, right);
        }

        public static BaseLiteDB.BsonValue Intersects(BaseLiteDB.BsonValue left, BaseLiteDB.BsonValue right)
        {
            var leftGeometry = ToGeometry(left);
            var rightGeometry = ToGeometry(right);

            if (leftGeometry == null || rightGeometry == null)
            {
                return False;
            }

            bool result = (leftGeometry, rightGeometry) switch
            {
                (GeoPolygon polygonA, GeoPolygon polygonB) => GeometryHelpers.Intersects(polygonA, polygonB),
                (GeoLineString line, GeoPolygon polygon) => GeometryHelpers.Intersects(line, polygon),
                (GeoPolygon polygon, GeoLineString line) => GeometryHelpers.Intersects(line, polygon),
                (GeoLineString lineA, GeoLineString lineB) => GeometryHelpers.Intersects(lineA, lineB),
                (GeoPoint point, GeoPolygon polygon) => GeometryHelpers.ContainsPoint(polygon, point),
                (GeoPolygon polygon, GeoPoint point) => GeometryHelpers.ContainsPoint(polygon, point),
                _ => false
            };

            return result ? True : False;
        }

        public static BaseLiteDB.BsonValue InvokeContains(
            BaseLiteDB.BsonDocument root,
            BaseLiteDB.Collation collation,
            BaseLiteDB.BsonDocument parameters,
            BaseLiteDB.BsonValue candidate,
            BaseLiteDB.BsonValue point)
        {
            return Contains(candidate, point);
        }

        public static BaseLiteDB.BsonValue Contains(BaseLiteDB.BsonValue candidate, BaseLiteDB.BsonValue point)
        {
            if (!TryGetGeoPoint(point, out var targetPoint))
            {
                return False;
            }

            var geometry = ToGeometry(candidate);

            if (geometry is GeoPolygon polygon)
            {
                return GeometryHelpers.ContainsPoint(polygon, targetPoint) ? True : False;
            }

            if (geometry is GeoLineString line)
            {
                return GeometryHelpers.LineContainsPoint(line, targetPoint) ? True : False;
            }

            if (geometry is BoundingBox box)
            {
                return BoundingBoxContains(targetPoint, box, allowWrap: true) ? True : False;
            }

            if (geometry is GeoPoint candidatePoint)
            {
                return candidatePoint.Equals(targetPoint) ? True : False;
            }

            return False;
        }

        public static BaseLiteDB.BsonValue InvokeInBox(
            BaseLiteDB.BsonDocument root,
            BaseLiteDB.Collation collation,
            BaseLiteDB.BsonDocument parameters,
            BaseLiteDB.BsonValue candidate,
            BaseLiteDB.BsonValue bounds)
        {
            return InBox(candidate, bounds);
        }

        public static BaseLiteDB.BsonValue InBox(BaseLiteDB.BsonValue candidate, BaseLiteDB.BsonValue bounds)
        {
            if (!TryGetBoundingBox(bounds, out var boundingBox))
            {
                return False;
            }

            if (TryGetGeoPoint(candidate, out var point2D))
            {
                return BoundingBoxContains(point2D, boundingBox, allowWrap: true) ? True : False;
            }

            if (TryGetGeoPoint3D(candidate, out var point3D))
            {
                return BoundingBoxContains(point3D, boundingBox, allowWrap: false) ? True : False;
            }

            var geometry = ToGeometry(candidate);
            if (geometry is GeoLineString line)
            {
                return PointsInsideBox(line.Points, boundingBox) ? True : False;
            }

            if (geometry is GeoPolygon polygon)
            {
                if (!PointsInsideBox(polygon.Outer, boundingBox))
                {
                    return False;
                }

                foreach (var hole in polygon.Holes)
                {
                    if (!PointsInsideBox(hole, boundingBox))
                    {
                        return False;
                    }
                }

                return True;
            }

            if (geometry is BoundingBox box)
            {
                return BoundingBoxWithin(box, boundingBox) ? True : False;
            }

            return False;
        }

        private static bool TryGetGeoPoint(BaseLiteDB.BsonValue value, out GeoPoint point)
        {
            if (value?.RawValue is GeoPoint directPoint)
            {
                point = directPoint;
                return true;
            }

            if (SpatialBsonParser.TryGetGeoPoint(value, out point))
            {
                return true;
            }

            point = default;
            return false;
        }

        private static bool TryGetGeoPoint3D(BaseLiteDB.BsonValue value, out GeoPoint3D point)
        {
            if (value?.RawValue is GeoPoint3D directPoint)
            {
                point = directPoint;
                return true;
            }

            if (SpatialBsonParser.TryGetGeoPoint3D(value, out point))
            {
                return true;
            }

            point = default;
            return false;
        }

        private static bool TryGetBoundingBox(BaseLiteDB.BsonValue value, out BoundingBox box)
        {
            if (value?.RawValue is BoundingBox directBox)
            {
                box = directBox;
                return true;
            }

            if (SpatialBsonParser.TryGetBoundingBox(value, out box))
            {
                return true;
            }

            box = default;
            return false;
        }

        private static object? ToGeometry(BaseLiteDB.BsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return null;
            }

            var raw = value.RawValue;
            if (raw is GeoPoint || raw is GeoPoint3D || raw is GeoLineString || raw is GeoPolygon || raw is BoundingBox)
            {
                return raw;
            }

            if (value.IsDocument)
            {
                try
                {
                    return GeoJsonSerializer.FromBson(value);
                }
                catch
                {
                    // ignore and attempt alternative parsing
                }
            }

            if (TryGetGeoPoint(value, out var point))
            {
                return point;
            }

            if (TryGetGeoPoint3D(value, out var point3D))
            {
                return point3D;
            }

            if (value.IsArray)
            {
                var array = value.AsArray;

                if (array.Count == 2 && array[0].IsNumber && array[1].IsNumber)
                {
                    return new GeoPoint(array[0].AsDouble, array[1].AsDouble);
                }

                if (array.Count == 3 && array[0].IsNumber && array[1].IsNumber && array[2].IsNumber)
                {
                    return new GeoPoint3D(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble);
                }

                if (array.Count == 4 || array.Count == 6)
                {
                    var values = new double[array.Count];
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (!array[i].IsNumber)
                        {
                            return null;
                        }

                        values[i] = array[i].AsDouble;
                    }

                    return BoundingBox.Create(values);
                }
            }

            return null;
        }

        private static GeographicDistanceMode ParseDistanceMode(BaseLiteDB.BsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return GeographicDistanceMode.Haversine;
            }

            if (value.IsString && Enum.TryParse(value.AsString, ignoreCase: true, out GeographicDistanceMode parsedMode))
            {
                return parsedMode;
            }

            if (value.IsInt32)
            {
                return value.AsInt32 switch
                {
                    1 => GeographicDistanceMode.Vincenty,
                    _ => GeographicDistanceMode.Haversine
                };
            }

            return GeographicDistanceMode.Haversine;
        }

        private static bool PointsInsideBox(IReadOnlyList<GeoPoint> points, BoundingBox bounds)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (!BoundingBoxContains(points[i], bounds, allowWrap: true))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool BoundingBoxWithin(BoundingBox candidate, BoundingBox container)
        {
            if (candidate.Dimensions != container.Dimensions)
            {
                return false;
            }

            var c = candidate.GetValues();
            var t = container.GetValues();

            if (candidate.Dimensions == 2)
            {
                return c[0] >= t[0] && c[2] <= t[2] && c[1] >= t[1] && c[3] <= t[3];
            }

            return c[0] >= t[0] && c[3] <= t[3] &&
                   c[1] >= t[1] && c[4] <= t[4] &&
                   c[2] >= t[2] && c[5] <= t[5];
        }

        private static bool BoundingBoxesIntersect(BoundingBox a, BoundingBox b)
        {
            if (a.Dimensions != b.Dimensions)
            {
                return false;
            }

            var av = a.GetValues();
            var bv = b.GetValues();

            if (a.Dimensions == 2)
            {
                return av[0] <= bv[2] && av[2] >= bv[0] &&
                       av[1] <= bv[3] && av[3] >= bv[1];
            }

            return av[0] <= bv[3] && av[3] >= bv[0] &&
                   av[1] <= bv[4] && av[4] >= bv[1] &&
                   av[2] <= bv[5] && av[5] >= bv[2];
        }

        private static bool BoundingBoxContains(GeoPoint point, BoundingBox bounds, bool allowWrap)
        {
            if (bounds.Dimensions != 2)
            {
                return false;
            }

            var values = bounds.GetValues();
            var minX = values[0];
            var minY = values[1];
            var maxX = values[2];
            var maxY = values[3];

            if (point.Latitude < minY || point.Latitude > maxY)
            {
                return false;
            }

            if (!allowWrap)
            {
                return point.Longitude >= minX && point.Longitude <= maxX;
            }

            if (maxX - minX >= LongitudeWrapWidth)
            {
                return true;
            }

            var normalizedLongitude = NormalizeLongitude(point.Longitude);
            foreach (var segment in SplitLongitudeRange(minX, maxX))
            {
                if (normalizedLongitude >= segment.min && normalizedLongitude <= segment.max)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BoundingBoxContains(GeoPoint3D point, BoundingBox bounds, bool allowWrap)
        {
            if (bounds.Dimensions == 2)
            {
                return BoundingBoxContains(new GeoPoint(point.X, point.Y), bounds, allowWrap);
            }

            var values = bounds.GetValues();
            return point.X >= values[0] && point.X <= values[3] &&
                   point.Y >= values[1] && point.Y <= values[4] &&
                   point.Z >= values[2] && point.Z <= values[5];
        }

        private static double NormalizeLongitude(double longitude)
        {
            var value = longitude % LongitudeWrapWidth;
            if (value <= -180d)
            {
                value += LongitudeWrapWidth;
            }
            else if (value > 180d)
            {
                value -= LongitudeWrapWidth;
            }

            return value;
        }

        private static IReadOnlyList<(double min, double max)> SplitLongitudeRange(double min, double max)
        {
            if (max - min >= LongitudeWrapWidth)
            {
                return new[] { (-180d, 180d) };
            }

            var normalizedMin = NormalizeLongitude(min);
            var offset = normalizedMin - min;
            var normalizedMax = max + offset;

            if (normalizedMax <= 180d)
            {
                return new[] { (normalizedMin, normalizedMax) };
            }

            return new[]
            {
                (normalizedMin, 180d),
                (-180d, normalizedMax - LongitudeWrapWidth)
            };
        }

        private static class GeometryHelpers
        {
            public static bool ContainsPoint(GeoPolygon polygon, GeoPoint point)
            {
                if (!IsPointInRing(polygon.Outer, point))
                {
                    return false;
                }

                foreach (var hole in polygon.Holes)
                {
                    if (IsPointInRing(hole, point))
                    {
                        return false;
                    }
                }

                return true;
            }

            public static bool LineContainsPoint(GeoLineString line, GeoPoint point)
            {
                for (var i = 0; i < line.Points.Count - 1; i++)
                {
                    var start = line.Points[i];
                    var end = line.Points[i + 1];

                    if (OnSegment(start, end, point) && Math.Abs(Direction(start, end, point)) < Epsilon)
                    {
                        return true;
                    }
                }

                return false;
            }

            public static bool LineWithinPolygon(GeoLineString line, GeoPolygon polygon)
            {
                foreach (var point in line.Points)
                {
                    if (!ContainsPoint(polygon, point))
                    {
                        return false;
                    }
                }

                for (var i = 0; i < line.Points.Count - 1; i++)
                {
                    var start = line.Points[i];
                    var end = line.Points[i + 1];

                    if (SegmentIntersectsRing(start, end, polygon.Outer, includeSharedEndpoints: false))
                    {
                        return false;
                    }

                    foreach (var hole in polygon.Holes)
                    {
                        if (SegmentIntersectsRing(start, end, hole, includeSharedEndpoints: true))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            public static bool PolygonWithinPolygon(GeoPolygon candidate, GeoPolygon container)
            {
                foreach (var point in candidate.Outer)
                {
                    if (!ContainsPoint(container, point))
                    {
                        return false;
                    }
                }

                foreach (var hole in candidate.Holes)
                {
                    foreach (var point in hole)
                    {
                        if (!ContainsPoint(container, point))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            public static bool Intersects(GeoPolygon a, GeoPolygon b)
            {
                var boxA = ComputeBoundingBox(a.Outer);
                var boxB = ComputeBoundingBox(b.Outer);

                if (!BoundingBoxesIntersect(boxA, boxB))
                {
                    return false;
                }

                foreach (var point in a.Outer)
                {
                    if (ContainsPoint(b, point))
                    {
                        return true;
                    }
                }

                foreach (var point in b.Outer)
                {
                    if (ContainsPoint(a, point))
                    {
                        return true;
                    }
                }

                if (RingsIntersect(a.Outer, b.Outer))
                {
                    return true;
                }

                foreach (var hole in a.Holes)
                {
                    if (RingsIntersect(hole, b.Outer))
                    {
                        return true;
                    }
                }

                foreach (var hole in b.Holes)
                {
                    if (RingsIntersect(a.Outer, hole))
                    {
                        return true;
                    }
                }

                return false;
            }

            public static bool Intersects(GeoLineString line, GeoPolygon polygon)
            {
                for (var i = 0; i < line.Points.Count - 1; i++)
                {
                    var start = line.Points[i];
                    var end = line.Points[i + 1];

                    if (ContainsPoint(polygon, start) || ContainsPoint(polygon, end))
                    {
                        return true;
                    }

                    if (SegmentIntersectsRing(start, end, polygon.Outer, includeSharedEndpoints: true))
                    {
                        return true;
                    }

                    foreach (var hole in polygon.Holes)
                    {
                        if (SegmentIntersectsRing(start, end, hole, includeSharedEndpoints: true))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public static bool Intersects(GeoLineString a, GeoLineString b)
            {
                for (var i = 0; i < a.Points.Count - 1; i++)
                {
                    for (var j = 0; j < b.Points.Count - 1; j++)
                    {
                        if (SegmentsIntersect(a.Points[i], a.Points[i + 1], b.Points[j], b.Points[j + 1], includeSharedEndpoints: true))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private static BoundingBox ComputeBoundingBox(IReadOnlyList<GeoPoint> ring)
            {
                var minLon = double.MaxValue;
                var maxLon = double.MinValue;
                var minLat = double.MaxValue;
                var maxLat = double.MinValue;

                for (var i = 0; i < ring.Count; i++)
                {
                    var point = ring[i];
                    if (point.Longitude < minLon) minLon = point.Longitude;
                    if (point.Longitude > maxLon) maxLon = point.Longitude;
                    if (point.Latitude < minLat) minLat = point.Latitude;
                    if (point.Latitude > maxLat) maxLat = point.Latitude;
                }

                return BoundingBox.From2D(minLon, minLat, maxLon, maxLat);
            }

            private static bool SegmentIntersectsRing(GeoPoint a, GeoPoint b, IReadOnlyList<GeoPoint> ring, bool includeSharedEndpoints)
            {
                for (var i = 0; i < ring.Count - 1; i++)
                {
                    if (SegmentsIntersect(a, b, ring[i], ring[i + 1], includeSharedEndpoints))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool RingsIntersect(IReadOnlyList<GeoPoint> a, IReadOnlyList<GeoPoint> b)
            {
                for (var i = 0; i < a.Count - 1; i++)
                {
                    for (var j = 0; j < b.Count - 1; j++)
                    {
                        if (SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1], includeSharedEndpoints: true))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private static bool SegmentsIntersect(GeoPoint a1, GeoPoint a2, GeoPoint b1, GeoPoint b2, bool includeSharedEndpoints)
            {
                var d1 = Direction(a1, a2, b1);
                var d2 = Direction(a1, a2, b2);
                var d3 = Direction(b1, b2, a1);
                var d4 = Direction(b1, b2, a2);

                if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                    ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                {
                    return true;
                }

                if (Math.Abs(d1) < Epsilon && OnSegment(a1, a2, b1))
                {
                    return includeSharedEndpoints || !IsEndpointShared(a1, a2, b1);
                }

                if (Math.Abs(d2) < Epsilon && OnSegment(a1, a2, b2))
                {
                    return includeSharedEndpoints || !IsEndpointShared(a1, a2, b2);
                }

                if (Math.Abs(d3) < Epsilon && OnSegment(b1, b2, a1))
                {
                    return includeSharedEndpoints || !IsEndpointShared(b1, b2, a1);
                }

                if (Math.Abs(d4) < Epsilon && OnSegment(b1, b2, a2))
                {
                    return includeSharedEndpoints || !IsEndpointShared(b1, b2, a2);
                }

                return false;
            }

            private static bool IsPointInRing(IReadOnlyList<GeoPoint> ring, GeoPoint point)
            {
                var inside = false;

                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    var pi = ring[i];
                    var pj = ring[j];

                    var intersects = ((pi.Latitude > point.Latitude) != (pj.Latitude > point.Latitude)) &&
                        (point.Longitude < (pj.Longitude - pi.Longitude) * (point.Latitude - pi.Latitude) / ((pj.Latitude - pi.Latitude) + double.Epsilon) + pi.Longitude);

                    if (intersects)
                    {
                        inside = !inside;
                    }
                }

                return inside;
            }

            private static bool OnSegment(GeoPoint a, GeoPoint b, GeoPoint c)
            {
                return c.Longitude >= Math.Min(a.Longitude, b.Longitude) - Epsilon &&
                       c.Longitude <= Math.Max(a.Longitude, b.Longitude) + Epsilon &&
                       c.Latitude >= Math.Min(a.Latitude, b.Latitude) - Epsilon &&
                       c.Latitude <= Math.Max(a.Latitude, b.Latitude) + Epsilon;
            }

            private static double Direction(GeoPoint a, GeoPoint b, GeoPoint c)
            {
                return (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
            }

            private static bool IsEndpointShared(GeoPoint start, GeoPoint end, GeoPoint point)
            {
                return (Math.Abs(start.Longitude - point.Longitude) < Epsilon && Math.Abs(start.Latitude - point.Latitude) < Epsilon) ||
                       (Math.Abs(end.Longitude - point.Longitude) < Epsilon && Math.Abs(end.Latitude - point.Latitude) < Epsilon);
            }
        }
    }
}

