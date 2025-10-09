extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps two-dimensional Cartesian points to normalized Morton coordinates.
/// </summary>
public sealed class Cartesian2DMapper : ISpatialMapper
{
    private readonly CartesianNormalizer _normalizer;
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;
    private readonly string _xFieldName;
    private readonly string _yFieldName;

    public Cartesian2DMapper(
        CartesianNormalizer normalizer,
        ISpatialIndexEncoder encoder,
        string geometryFieldName,
        string xFieldName = "x",
        string yFieldName = "y")
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName))
            : geometryFieldName;
        _xFieldName = string.IsNullOrWhiteSpace(xFieldName) ? "x" : xFieldName;
        _yFieldName = string.IsNullOrWhiteSpace(yFieldName) ? "y" : yFieldName;
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper cannot project three-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        var normalized = _normalizer.NormalizePoint2D(point.Longitude, point.Latitude);
        Span<double> buffer = stackalloc double[2];
        buffer[0] = normalized.x;
        buffer[1] = normalized.y;
        return _encoder.Encode(buffer);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper cannot project three-dimensional points.");
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
        if (!TryReadDouble(geometryDoc, _xFieldName, out var x) || !TryReadDouble(geometryDoc, _yFieldName, out var y))
        {
            point = default;
            return false;
        }

        point = new GeoPoint(x, y);
        return true;
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;
        return false;
    }

    private static bool TryReadDouble(BaseLiteDB.BsonDocument document, string field, out double value)
    {
        if (document.TryGetValue(field, out var bson) && bson.IsNumber)
        {
            value = bson.AsDouble;
            return true;
        }

        value = default;
        return false;
    }
}
