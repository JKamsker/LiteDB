extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class GeographicMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    public GeographicMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName))
            : geometryFieldName;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(
            GeographicMath.ToNormalizedLongitude(point.Longitude),
            GeographicMath.ToNormalizedLatitude(point.Latitude),
            GeographicMath.ToNormalizedLongitude(point.Longitude),
            GeographicMath.ToNormalizedLatitude(point.Latitude));
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapping is restricted to two-dimensional coordinates.");
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = GeographicMath.ToNormalizedLongitude(point.Longitude);
        coordinates[1] = GeographicMath.ToNormalizedLatitude(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapping is restricted to two-dimensional coordinates.");
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

        if (geometry.IsArray)
        {
            var array = geometry.AsArray;
            if (array.Count < 2 || !array[0].IsNumber || !array[1].IsNumber)
            {
                throw new SpatialMetadataException($"Geometry field '{_geometryFieldName}' must contain two numeric values when stored as an array.");
            }

            point = GeographicMath.NormalizePoint(new GeoPoint(array[0].AsDouble, array[1].AsDouble));
            return true;
        }

        if (geometry.IsDocument)
        {
            var geoDoc = geometry.AsDocument;
            if (!TryReadDocumentCoordinates(geoDoc, out var longitude, out var latitude))
            {
                throw new SpatialMetadataException($"Geometry field '{_geometryFieldName}' must expose longitude and latitude members.");
            }

            point = GeographicMath.NormalizePoint(new GeoPoint(longitude, latitude));
            return true;
        }

        throw new SpatialMetadataException($"Geometry field '{_geometryFieldName}' must be either an array or document containing coordinates.");
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static bool TryReadDocumentCoordinates(BaseLiteDB.BsonDocument document, out double longitude, out double latitude)
    {
        longitude = 0d;
        latitude = 0d;

        if (!TryRead(document, new[] { "longitude", "lon", "lng", "x" }, out longitude))
        {
            return false;
        }

        if (!TryRead(document, new[] { "latitude", "lat", "y" }, out latitude))
        {
            return false;
        }

        return true;
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
