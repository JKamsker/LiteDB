extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps application level geographic data into index friendly representations.
/// </summary>
public sealed class GeographicMapper : ISpatialMapper
{
    private readonly string _geometryField;
    private readonly ISpatialIndexEncoder _encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicMapper"/> class.
    /// </summary>
    public GeographicMapper(string geometryField, ISpatialIndexEncoder encoder)
    {
        if (string.IsNullOrWhiteSpace(geometryField))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryField));
        }

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryField = geometryField;
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper only supports two-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = GeographicHelpers.LongitudeToUnit(point.Longitude);
        coordinates[1] = GeographicHelpers.LatitudeToUnit(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper only supports two-dimensional points.");
    }

    /// <inheritdoc />
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

        if (TryReadPoint(value, out point))
        {
            return true;
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

    private static bool TryReadPoint(BaseLiteDB.BsonValue value, out GeoPoint point)
    {
        if (value.IsDocument)
        {
            var document = value.AsDocument;

            if (TryReadGeoJsonPoint(document, out point))
            {
                return true;
            }

            if (TryReadCoordinatePair(document, out point))
            {
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

    private static bool TryReadGeoJsonPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (document.TryGetValue("type", out var typeValue)
            && typeValue.IsString
            && string.Equals(typeValue.AsString, "Point", StringComparison.OrdinalIgnoreCase)
            && document.TryGetValue("coordinates", out var coordinatesValue)
            && coordinatesValue.IsArray)
        {
            var coordinates = coordinatesValue.AsArray;
            if (coordinates.Count >= 2 && coordinates[0].IsNumber && coordinates[1].IsNumber)
            {
                point = new GeoPoint(coordinates[0].AsDouble, coordinates[1].AsDouble);
                return true;
            }
        }

        point = default;
        return false;
    }

    private static bool TryReadCoordinatePair(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (TryGetNumeric(document, "longitude", out var lon) && TryGetNumeric(document, "latitude", out var lat))
        {
            point = new GeoPoint(lon, lat);
            return true;
        }

        if (TryGetNumeric(document, "lon", out lon) && TryGetNumeric(document, "lat", out lat))
        {
            point = new GeoPoint(lon, lat);
            return true;
        }

        if (TryGetNumeric(document, "lng", out lon) && TryGetNumeric(document, "lat", out lat))
        {
            point = new GeoPoint(lon, lat);
            return true;
        }

        if (TryGetNumeric(document, "x", out lon) && TryGetNumeric(document, "y", out lat))
        {
            point = new GeoPoint(lon, lat);
            return true;
        }

        point = default;
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
