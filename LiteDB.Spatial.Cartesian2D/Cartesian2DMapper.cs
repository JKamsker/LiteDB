extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class Cartesian2DMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;
    private readonly double _minX;
    private readonly double _maxX;
    private readonly double _minY;
    private readonly double _maxY;
    private readonly double _rangeX;
    private readonly double _rangeY;

    public Cartesian2DMapper(
        ISpatialIndexEncoder encoder,
        BoundingBox coordinateSpace,
        string geometryFieldName)
    {
        if (coordinateSpace.Dimensions != 2)
        {
            throw new ArgumentException("Coordinate space must be two-dimensional.", nameof(coordinateSpace));
        }

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;

        _minX = coordinateSpace.MinX;
        _maxX = coordinateSpace.MaxX;
        _minY = coordinateSpace.MinY;
        _maxY = coordinateSpace.MaxY;

        _rangeX = _maxX - _minX;
        _rangeY = _maxY - _minY;

        if (_rangeX <= 0d || _rangeY <= 0d)
        {
            throw new ArgumentException("Coordinate space must have positive extents on all axes.", nameof(coordinateSpace));
        }
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper cannot produce three-dimensional bounding boxes.");
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = Normalize(point.Longitude, _minX, _rangeX);
        coordinates[1] = Normalize(point.Latitude, _minY, _rangeY);
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper cannot encode three-dimensional coordinates.");
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryFieldName, out var value))
        {
            point = default;
            return false;
        }

        if (TryExtract(value, out point))
        {
            return true;
        }

        point = default;
        return false;
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static bool TryExtract(BaseLiteDB.BsonValue value, out GeoPoint point)
    {
        if (value.IsArray)
        {
            var array = value.AsArray;
            if (array.Count >= 2 && TryReadDouble(array[0], out var x) && TryReadDouble(array[1], out var y))
            {
                point = new GeoPoint(x, y);
                return true;
            }
        }
        else if (value.IsDocument)
        {
            var doc = value.AsDocument;

            if (doc.TryGetValue("coordinates", out var coordinates) && coordinates.IsArray)
            {
                var array = coordinates.AsArray;
                if (array.Count >= 2 && TryReadDouble(array[0], out var x) && TryReadDouble(array[1], out var y))
                {
                    point = new GeoPoint(x, y);
                    return true;
                }
            }

            if (TryReadDouble(doc, new[] { "x", "longitude", "lon", "lng" }, out var xValue)
                && TryReadDouble(doc, new[] { "y", "latitude", "lat" }, out var yValue))
            {
                point = new GeoPoint(xValue, yValue);
                return true;
            }
        }

        point = default;
        return false;
    }

    private static bool TryReadDouble(BaseLiteDB.BsonDocument document, IReadOnlyList<string> keys, out double value)
    {
        foreach (var key in keys)
        {
            if (document.TryGetValue(key, out var candidate) && TryReadDouble(candidate, out value))
            {
                return true;
            }
        }

        value = 0d;
        return false;
    }

    private static bool TryReadDouble(BaseLiteDB.BsonValue value, out double result)
    {
        if (value.IsInt32)
        {
            result = value.AsInt32;
            return true;
        }

        if (value.IsInt64)
        {
            result = value.AsInt64;
            return true;
        }

        if (value.IsDecimal)
        {
            result = (double)value.AsDecimal;
            return true;
        }

        if (value.IsDouble)
        {
            result = value.AsDouble;
            return true;
        }

        result = 0d;
        return false;
    }

    private static double Normalize(double value, double minimum, double range)
    {
        var normalized = (value - minimum) / range;

        if (normalized < 0d)
        {
            return 0d;
        }

        if (normalized > 1d)
        {
            return 1d;
        }

        return normalized;
    }
}
