extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps geographic points from application documents into index-friendly representations.
/// </summary>
public sealed class GeographicMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicMapper"/> class.
    /// </summary>
    /// <param name="encoder">The index encoder used to translate coordinates.</param>
    /// <param name="geometryFieldName">The field that contains geographic coordinates.</param>
    public GeographicMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper only handles two-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = GeographicIndexing.NormalizeLongitude(point.Longitude);
        coordinates[1] = GeographicIndexing.NormalizeLatitude(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper only handles two-dimensional points.");
    }

    /// <inheritdoc />
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

        if (TryFromArray(geometry, out point))
        {
            return true;
        }

        if (geometry.IsDocument)
        {
            var geoDoc = geometry.AsDocument;

            if (geoDoc.TryGetValue("coordinates", out var coordinates) && TryFromArray(coordinates, out point))
            {
                return true;
            }

            if (TryGetNumber(geoDoc, "longitude", out var lon) || TryGetNumber(geoDoc, "lon", out lon) || TryGetNumber(geoDoc, "x", out lon))
            {
                if (TryGetNumber(geoDoc, "latitude", out var lat) || TryGetNumber(geoDoc, "lat", out lat) || TryGetNumber(geoDoc, "y", out lat))
                {
                    point = new GeoPoint(lon, lat);
                    return true;
                }
            }
        }

        point = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static bool TryFromArray(BaseLiteDB.BsonValue value, out GeoPoint point)
    {
        if (value.IsArray && value.AsArray.Count >= 2 && value.AsArray[0].IsNumber && value.AsArray[1].IsNumber)
        {
            var lon = value.AsArray[0].AsDouble;
            var lat = value.AsArray[1].AsDouble;
            point = new GeoPoint(lon, lat);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryGetNumber(BaseLiteDB.BsonDocument document, string name, out double result)
    {
        if (document.TryGetValue(name, out var value) && value.IsNumber)
        {
            result = value.AsDouble;
            return true;
        }

        result = default;
        return false;
    }
}
