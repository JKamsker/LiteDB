#nullable enable

extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps geographic values into index and bounding box representations.
/// </summary>
public sealed class GeographicMapper : ISpatialMapper
{
    private static readonly string[] LongitudeKeys = { "longitude", "lon", "lng", "x" };
    private static readonly string[] LatitudeKeys = { "latitude", "lat", "y" };

    private readonly string _geometryFieldName;
    private readonly ISpatialIndexEncoder _encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeographicMapper"/> class.
    /// </summary>
    /// <param name="geometryFieldName">Name of the document field that stores the geometry.</param>
    /// <param name="encoder">Encoder used to generate Morton codes.</param>
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
        var longitude = GeographicMath.NormalizeLongitude(point.Longitude);
        var latitude = GeographicMath.ClampLatitude(point.Latitude);
        return BoundingBox.From2D(longitude, latitude, longitude, latitude);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper supports only two-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = GeographicMath.LongitudeToUnit(point.Longitude);
        coordinates[1] = GeographicMath.LatitudeToUnit(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Geographic mapper supports only two-dimensional points.");
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            return false;
        }

        if (TryReadFromDocument(geometry, out point))
        {
            return true;
        }

        if (TryReadFromArray(geometry, out point))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static bool TryReadFromDocument(BaseLiteDB.BsonValue value, out GeoPoint point)
    {
        point = default;

        if (!value.IsDocument)
        {
            return false;
        }

        var doc = value.AsDocument;

        if (doc.TryGetValue("type", out var typeValue) && typeValue.IsString && string.Equals(typeValue.AsString, "Point", StringComparison.OrdinalIgnoreCase))
        {
            if (doc.TryGetValue("coordinates", out var coordinates) && TryReadFromArray(coordinates, out point))
            {
                return true;
            }
        }

        if (TryReadCoordinatePair(doc, out var longitude, out var latitude))
        {
            point = new GeoPoint(longitude, latitude);
            return true;
        }

        return false;
    }

    private static bool TryReadFromArray(BaseLiteDB.BsonValue value, out GeoPoint point)
    {
        point = default;

        if (!value.IsArray)
        {
            return false;
        }

        var array = value.AsArray;
        if (array.Count < 2 || !array[0].IsNumber || !array[1].IsNumber)
        {
            return false;
        }

        point = new GeoPoint(array[0].AsDouble, array[1].AsDouble);
        return true;
    }

    private static bool TryReadCoordinatePair(BaseLiteDB.BsonDocument doc, out double longitude, out double latitude)
    {
        foreach (var key in LongitudeKeys)
        {
            if (doc.TryGetValue(key, out var lonValue) && lonValue.IsNumber)
            {
                longitude = lonValue.AsDouble;

                foreach (var latKey in LatitudeKeys)
                {
                    if (doc.TryGetValue(latKey, out var latValue) && latValue.IsNumber)
                    {
                        latitude = latValue.AsDouble;
                        return true;
                    }
                }
            }
        }

        longitude = 0d;
        latitude = 0d;
        return false;
    }
}
