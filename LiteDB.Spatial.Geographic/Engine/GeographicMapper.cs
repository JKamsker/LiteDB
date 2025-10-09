extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides mapping helpers for geographic points stored inside <see cref="BaseLiteDB.BsonDocument"/> values.
/// </summary>
public sealed class GeographicMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicMapper"/> class.
    /// </summary>
    /// <param name="encoder">The encoder responsible for converting normalized coordinates into Morton codes.</param>
    /// <param name="geometryFieldName">The field that stores the geometry document.</param>
    public GeographicMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName))
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
        throw new NotSupportedException("Geographic mapping only supports two-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        var normalized = Normalize(point);
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = normalized.longitude;
        coordinates[1] = normalized.latitude;
        return _encoder.Encode(coordinates);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapping only supports two-dimensional points.");
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || !geometry.IsDocument)
        {
            point = default;
            return false;
        }

        var geometryDoc = geometry.AsDocument;

        if (!TryReadDouble(geometryDoc, "longitude", out var longitude) && !TryReadDouble(geometryDoc, "lon", out longitude))
        {
            point = default;
            return false;
        }

        if (!TryReadDouble(geometryDoc, "latitude", out var latitude) && !TryReadDouble(geometryDoc, "lat", out latitude))
        {
            point = default;
            return false;
        }

        try
        {
            point = new GeoPoint(longitude, latitude);
            return true;
        }
        catch (ArgumentException)
        {
            point = default;
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static (double longitude, double latitude) Normalize(GeoPoint point)
    {
        var normalizedLongitude = (NormalizeLongitude(point.Longitude) + 180d) / 360d;
        var normalizedLatitude = (ClampLatitude(point.Latitude) + 90d) / 180d;
        return (normalizedLongitude, normalizedLatitude);
    }

    private static double NormalizeLongitude(double longitude)
    {
        var value = longitude % 360d;
        if (value <= -180d)
        {
            value += 360d;
        }
        else if (value > 180d)
        {
            value -= 360d;
        }

        return value;
    }

    private static double ClampLatitude(double latitude)
    {
        if (latitude < -90d)
        {
            return -90d;
        }

        if (latitude > 90d)
        {
            return 90d;
        }

        return latitude;
    }

    private static bool TryReadDouble(BaseLiteDB.BsonDocument document, string field, out double value)
    {
        if (document.TryGetValue(field, out var bson) && bson.IsNumber)
        {
            value = bson.AsDouble;
            return true;
        }

        if (TryGetAlternateField(document, field, out bson))
        {
            value = bson.AsDouble;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetAlternateField(BaseLiteDB.BsonDocument document, string field, out BaseLiteDB.BsonValue value)
    {
        value = default!;

        if (string.IsNullOrEmpty(field))
        {
            return false;
        }

        if (char.IsLower(field[0]))
        {
            var alternate = char.ToUpperInvariant(field[0]) + field.Substring(1);
            if (document.TryGetValue(alternate, out value) && value.IsNumber)
            {
                return true;
            }
        }
        else if (char.IsUpper(field[0]))
        {
            var alternate = char.ToLowerInvariant(field[0]) + field.Substring(1);
            if (document.TryGetValue(alternate, out value) && value.IsNumber)
            {
                return true;
            }
        }

        return false;
    }
}
