extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class Cartesian3DMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;
    private readonly double _minX;
    private readonly double _maxX;
    private readonly double _minY;
    private readonly double _maxY;
    private readonly double _minZ;
    private readonly double _maxZ;
    private readonly double _rangeX;
    private readonly double _rangeY;
    private readonly double _rangeZ;

    public Cartesian3DMapper(
        ISpatialIndexEncoder encoder,
        BoundingBox coordinateSpace,
        string geometryFieldName)
    {
        if (coordinateSpace.Dimensions != 3)
        {
            throw new ArgumentException("Coordinate space must be three-dimensional.", nameof(coordinateSpace));
        }

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;

        _minX = coordinateSpace.MinX;
        _maxX = coordinateSpace.MaxX;
        _minY = coordinateSpace.MinY;
        _maxY = coordinateSpace.MaxY;
        _minZ = coordinateSpace.MinZ;
        _maxZ = coordinateSpace.MaxZ;

        _rangeX = _maxX - _minX;
        _rangeY = _maxY - _minY;
        _rangeZ = _maxZ - _minZ;

        if (_rangeX <= 0d || _rangeY <= 0d || _rangeZ <= 0d)
        {
            throw new ArgumentException("Coordinate space must have positive extents on all axes.", nameof(coordinateSpace));
        }
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper does not support 2D bounding boxes.");
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
    }

    public ulong Encode(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper does not support 2D encoding.");
    }

    public ulong Encode(GeoPoint3D point)
    {
        Span<double> coordinates = stackalloc double[3];
        coordinates[0] = Normalize(point.X, _minX, _rangeX);
        coordinates[1] = Normalize(point.Y, _minY, _rangeY);
        coordinates[2] = Normalize(point.Z, _minZ, _rangeZ);
        return _encoder.Encode(coordinates);
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;
        return false;
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
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

    private static bool TryExtract(BaseLiteDB.BsonValue value, out GeoPoint3D point)
    {
        if (value.IsArray)
        {
            var array = value.AsArray;
            if (array.Count >= 3
                && TryReadDouble(array[0], out var x)
                && TryReadDouble(array[1], out var y)
                && TryReadDouble(array[2], out var z))
            {
                point = new GeoPoint3D(x, y, z);
                return true;
            }
        }
        else if (value.IsDocument)
        {
            var doc = value.AsDocument;

            if (doc.TryGetValue("coordinates", out var coordinates) && coordinates.IsArray)
            {
                var array = coordinates.AsArray;
                if (array.Count >= 3
                    && TryReadDouble(array[0], out var x)
                    && TryReadDouble(array[1], out var y)
                    && TryReadDouble(array[2], out var z))
                {
                    point = new GeoPoint3D(x, y, z);
                    return true;
                }
            }

            if (TryReadDouble(doc, new[] { "x", "longitude", "lon", "lng" }, out var xValue)
                && TryReadDouble(doc, new[] { "y", "latitude", "lat" }, out var yValue)
                && TryReadDouble(doc, new[] { "z", "alt", "elevation" }, out var zValue))
            {
                point = new GeoPoint3D(xValue, yValue, zValue);
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
