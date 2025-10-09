extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class Cartesian3DMapper : ISpatialMapper
{
    private readonly string _geometryField;
    private readonly ISpatialIndexEncoder _encoder;

    public Cartesian3DMapper(string geometryField, ISpatialIndexEncoder encoder)
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
        throw new NotSupportedException("Cartesian3D mapper does not support two-dimensional points.");
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
    }

    public ulong Encode(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper does not support two-dimensional points.");
    }

    public ulong Encode(GeoPoint3D point)
    {
        Span<double> coordinates = stackalloc double[3];
        coordinates[0] = Cartesian3DHelpers.Clamp01(point.X);
        coordinates[1] = Cartesian3DHelpers.Clamp01(point.Y);
        coordinates[2] = Cartesian3DHelpers.Clamp01(point.Z);
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

        if (!document.TryGetValue(_geometryField, out var value) || value.IsNull)
        {
            point = default;
            return false;
        }

        if (value.IsDocument)
        {
            var geometry = value.AsDocument;
            if (TryReadComponents(geometry, out var x, out var y, out var z))
            {
                point = new GeoPoint3D(x, y, z);
                return true;
            }
        }
        else if (value.IsArray)
        {
            var array = value.AsArray;
            if (array.Count >= 3 && array[0].IsNumber && array[1].IsNumber && array[2].IsNumber)
            {
                point = new GeoPoint3D(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble);
                return true;
            }
        }

        point = default;
        return false;
    }

    private static bool TryReadComponents(BaseLiteDB.BsonDocument document, out double x, out double y, out double z)
    {
        if (TryGetNumeric(document, "x", out x) && TryGetNumeric(document, "y", out y) && TryGetNumeric(document, "z", out z))
        {
            return true;
        }

        if (TryGetNumeric(document, "lon", out x) && TryGetNumeric(document, "lat", out y) && TryGetNumeric(document, "alt", out z))
        {
            return true;
        }

        if (TryGetNumeric(document, "longitude", out x) && TryGetNumeric(document, "latitude", out y) && TryGetNumeric(document, "elevation", out z))
        {
            return true;
        }

        x = default;
        y = default;
        z = default;
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
