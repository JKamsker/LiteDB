#nullable enable

extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps two-dimensional Cartesian points for indexing.
/// </summary>
public sealed class Cartesian2DMapper : ISpatialMapper
{
    private readonly string _geometryFieldName;
    private readonly ISpatialIndexEncoder _encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian2DMapper"/> class.
    /// </summary>
    public Cartesian2DMapper(string geometryFieldName, ISpatialIndexEncoder encoder)
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
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper cannot operate on three-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = point.Longitude;
        coordinates[1] = point.Latitude;
        return _encoder.Encode(coordinates);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper cannot operate on three-dimensional points.");
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;

        if (!document.TryGetValue(_geometryFieldName, out var value) || value.IsNull)
        {
            return false;
        }

        if (value.IsDocument)
        {
            var doc = value.AsDocument;
            if (TryReadCoordinatePair(doc, out var x, out var y))
            {
                point = new GeoPoint(x, y);
                return true;
            }

            if (doc.TryGetValue("coordinates", out var arrayValue) && TryReadFromArray(arrayValue, out x, out y))
            {
                point = new GeoPoint(x, y);
                return true;
            }
        }
        else if (TryReadFromArray(value, out var x, out var y))
        {
            point = new GeoPoint(x, y);
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

    private static bool TryReadCoordinatePair(BaseLiteDB.BsonDocument document, out double x, out double y)
    {
        if (document.TryGetValue("x", out var xValue) && document.TryGetValue("y", out var yValue)
            && xValue.IsNumber && yValue.IsNumber)
        {
            x = xValue.AsDouble;
            y = yValue.AsDouble;
            return true;
        }

        x = 0d;
        y = 0d;
        return false;
    }

    private static bool TryReadFromArray(BaseLiteDB.BsonValue value, out double x, out double y)
    {
        if (!value.IsArray)
        {
            x = 0d;
            y = 0d;
            return false;
        }

        var array = value.AsArray;
        if (array.Count < 2 || !array[0].IsNumber || !array[1].IsNumber)
        {
            x = 0d;
            y = 0d;
            return false;
        }

        x = array[0].AsDouble;
        y = array[1].AsDouble;
        return true;
    }
}
