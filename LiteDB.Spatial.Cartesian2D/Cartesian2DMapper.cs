extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class Cartesian2DMapper : ISpatialMapper
{
    private readonly string _geometryField;
    private readonly ISpatialIndexEncoder _encoder;

    public Cartesian2DMapper(string geometryField, ISpatialIndexEncoder encoder)
    {
        if (string.IsNullOrWhiteSpace(geometryField))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryField));
        }

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryField = geometryField;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper does not handle three-dimensional points.");
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = CartesianHelpers.Clamp01(point.Longitude);
        coordinates[1] = CartesianHelpers.Clamp01(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper does not handle three-dimensional points.");
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryField, out var value) || value.IsNull)
        {
            point = default;
            return false;
        }

        if (value.IsDocument)
        {
            var geometry = value.AsDocument;
            if (TryReadComponents(geometry, out var x, out var y))
            {
                point = new GeoPoint(x, y);
                return true;
            }
        }
        else if (value.IsArray)
        {
            var array = value.AsArray;
            if (array.Count >= 2 && array[0].IsNumber && array[1].IsNumber)
            {
                point = new GeoPoint(array[0].AsDouble, array[1].AsDouble);
                return true;
            }
        }

        point = default;
        return false;
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static bool TryReadComponents(BaseLiteDB.BsonDocument document, out double x, out double y)
    {
        if (TryGetNumeric(document, "x", out x) && TryGetNumeric(document, "y", out y))
        {
            return true;
        }

        if (TryGetNumeric(document, "lon", out x) && TryGetNumeric(document, "lat", out y))
        {
            return true;
        }

        if (TryGetNumeric(document, "longitude", out x) && TryGetNumeric(document, "latitude", out y))
        {
            return true;
        }

        y = default;
        return false;
    }

    private static bool TryGetNumeric(BaseLiteDB.BsonDocument document, string key, out double value)
    {
        if (document.TryGetValue(key, out var bsonValue) && bsonValue.IsNumber)
        {
            value = bsonValue.AsDouble;
            return true;
        }

        value = default;
        return false;
    }
}
