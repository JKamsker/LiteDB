extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class Cartesian2DMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    public Cartesian2DMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName))
            : geometryFieldName;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From2D(point.X, point.Y, point.X, point.Y);
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = point.Longitude;
        coordinates[1] = point.Latitude;
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = point.X;
        coordinates[1] = point.Y;
        return _encoder.Encode(coordinates);
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            point = default;
            return false;
        }

        if (TryReadComponents(geometry, out var x, out var y, out _))
        {
            point = new GeoPoint(x, y);
            return true;
        }

        throw new SpatialMetadataException($"Geometry field '{_geometryFieldName}' must contain numeric X/Y coordinates.");
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            point = default;
            return false;
        }

        if (TryReadComponents(geometry, out var x, out var y, out var z))
        {
            point = new GeoPoint3D(x, y, z);
            return true;
        }

        throw new SpatialMetadataException($"Geometry field '{_geometryFieldName}' must contain numeric X/Y coordinates.");
    }

    internal static bool TryReadComponents(BaseLiteDB.BsonValue geometry, out double x, out double y, out double z)
    {
        x = y = z = 0d;

        if (geometry.IsArray)
        {
            var array = geometry.AsArray;
            if (array.Count < 2 || !array[0].IsNumber || !array[1].IsNumber)
            {
                return false;
            }

            x = array[0].AsDouble;
            y = array[1].AsDouble;
            z = array.Count > 2 && array[2].IsNumber ? array[2].AsDouble : 0d;
            return true;
        }

        if (geometry.IsDocument)
        {
            var doc = geometry.AsDocument;
            if (!TryRead(doc, new[] { "x", "longitude", "lon" }, out x))
            {
                return false;
            }

            if (!TryRead(doc, new[] { "y", "latitude", "lat" }, out y))
            {
                return false;
            }

            TryRead(doc, new[] { "z" }, out z);
            return true;
        }

        return false;
    }

    private static bool TryRead(BaseLiteDB.BsonDocument document, string[] candidates, out double value)
    {
        foreach (var name in candidates)
        {
            if (document.TryGetValue(name, out var bson) && bson.IsNumber)
            {
                value = bson.AsDouble;
                return true;
            }
        }

        value = 0d;
        return false;
    }
}
