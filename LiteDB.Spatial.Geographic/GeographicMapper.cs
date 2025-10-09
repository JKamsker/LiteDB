extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Projects geographic coordinates into Morton index space and extracts them from stored documents.
/// </summary>
internal sealed class GeographicMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    public GeographicMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        var longitude = GeographicMath.NormalizeLongitude(point.Longitude);
        var latitude = GeographicMath.NormalizeLatitude(point.Latitude);
        return BoundingBox.From2D(longitude, latitude, longitude, latitude);
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper does not support three-dimensional coordinates.");
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = GeographicMath.LongitudeToUnit(point.Longitude);
        coordinates[1] = GeographicMath.LatitudeToUnit(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper does not support three-dimensional coordinates.");
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

        if (TryReadPoint(value, out point))
        {
            point = new GeoPoint(
                GeographicMath.NormalizeLongitude(point.Longitude),
                GeographicMath.NormalizeLatitude(point.Latitude));
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

    private static bool TryReadPoint(BaseLiteDB.BsonValue value, out GeoPoint point)
    {
        if (value.IsArray)
        {
            var array = value.AsArray;
            if (array.Count >= 2 && TryReadDouble(array[0], out var lon) && TryReadDouble(array[1], out var lat))
            {
                point = new GeoPoint(lon, lat);
                return true;
            }
        }
        else if (value.IsDocument)
        {
            var doc = value.AsDocument;

            if (doc.TryGetValue("coordinates", out var coordinates) && coordinates.IsArray)
            {
                var array = coordinates.AsArray;
                if (array.Count >= 2 && TryReadDouble(array[0], out var lon) && TryReadDouble(array[1], out var lat))
                {
                    point = new GeoPoint(lon, lat);
                    return true;
                }
            }

            if (TryReadDouble(doc, out var lonValue, out var latValue))
            {
                point = new GeoPoint(lonValue, latValue);
                return true;
            }
        }

        point = default;
        return false;
    }

    private static bool TryReadDouble(BaseLiteDB.BsonDocument document, out double lon, out double lat)
    {
        lon = 0d;
        lat = 0d;

        if (!TryReadDouble(document, new[] { "longitude", "lon", "lng", "x" }, out lon))
        {
            return false;
        }

        if (!TryReadDouble(document, new[] { "latitude", "lat", "y" }, out lat))
        {
            return false;
        }

        return true;
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
}
