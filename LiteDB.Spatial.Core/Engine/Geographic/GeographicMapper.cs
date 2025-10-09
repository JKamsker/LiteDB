extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Maps geographic values from documents into Morton encoded index entries.
/// </summary>
public sealed class GeographicMapper : ISpatialMapper
{
    private readonly string _geometryFieldName;
    private readonly ISpatialIndexEncoder _encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicMapper"/> class.
    /// </summary>
    /// <param name="geometryFieldName">The document field that stores the coordinate.</param>
    /// <param name="encoder">The encoder responsible for quantising coordinates.</param>
    public GeographicMapper(string geometryFieldName, ISpatialIndexEncoder encoder)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        _geometryFieldName = geometryFieldName;
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        ValidateCoordinate(point.Longitude, point.Latitude);
        var lon = GeographicMath.NormalizeLongitude(point.Longitude);
        var lat = GeographicMath.ClampLatitude(point.Latitude);
        return BoundingBox.From2D(lon, lat, lon, lat);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapping is limited to two-dimensional coordinates.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        ValidateCoordinate(point.Longitude, point.Latitude);
        Span<double> values = stackalloc double[2];
        values[0] = GeographicMath.NormalizeLongitudeToUnit(point.Longitude);
        values[1] = GeographicMath.NormalizeLatitudeToUnit(point.Latitude);
        return _encoder.Encode(values);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapping is limited to two-dimensional coordinates.");
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            return false;
        }

        if (!TryParseLongitudeLatitude(geometry, out var longitude, out var latitude))
        {
            return false;
        }

        ValidateCoordinate(longitude, latitude);
        point = new GeoPoint(longitude, latitude);
        return true;
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static void ValidateCoordinate(double longitude, double latitude)
    {
        if (double.IsNaN(longitude) || double.IsInfinity(longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be a finite number.");
        }

        if (double.IsNaN(latitude) || double.IsInfinity(latitude))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be a finite number.");
        }

        if (latitude < -90d || latitude > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be within [-90, 90] degrees.");
        }

        if (longitude < -720d || longitude > 720d)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude exceeds supported wraparound bounds.");
        }
    }

    private static bool TryParseLongitudeLatitude(BaseLiteDB.BsonValue geometry, out double longitude, out double latitude)
    {
        longitude = 0d;
        latitude = 0d;

        if (geometry.IsDocument)
        {
            var doc = geometry.AsDocument;

            if (TryReadDouble(doc, "longitude", out longitude) && TryReadDouble(doc, "latitude", out latitude))
            {
                return true;
            }

            if (TryReadDouble(doc, "lon", out longitude) && TryReadDouble(doc, "lat", out latitude))
            {
                return true;
            }

            if (TryReadDouble(doc, "lng", out longitude) && TryReadDouble(doc, "lat", out latitude))
            {
                return true;
            }

            if (doc.TryGetValue("coordinates", out var coordinates) && coordinates.IsArray && coordinates.AsArray.Count >= 2)
            {
                return TryReadArray(coordinates.AsArray, out longitude, out latitude);
            }
        }
        else if (geometry.IsArray)
        {
            return TryReadArray(geometry.AsArray, out longitude, out latitude);
        }

        return false;
    }

    private static bool TryReadArray(BaseLiteDB.BsonArray array, out double longitude, out double latitude)
    {
        longitude = 0d;
        latitude = 0d;

        if (array.Count < 2)
        {
            return false;
        }

        if (!TryReadDouble(array[0], out longitude) || !TryReadDouble(array[1], out latitude))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadDouble(BaseLiteDB.BsonDocument document, string field, out double value)
    {
        value = 0d;

        if (!document.TryGetValue(field, out var bson) || bson.IsNull)
        {
            return false;
        }

        return TryReadDouble(bson, out value);
    }

    private static bool TryReadDouble(BaseLiteDB.BsonValue value, out double result)
    {
        if (value.IsDouble)
        {
            result = value.AsDouble;
            return true;
        }

        if (value.IsDecimal)
        {
            result = (double)value.AsDecimal;
            return true;
        }

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

        result = 0d;
        return false;
    }
}

