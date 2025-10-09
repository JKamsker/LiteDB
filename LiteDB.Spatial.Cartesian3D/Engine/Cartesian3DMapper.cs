extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps three-dimensional Cartesian points to normalized Morton coordinates.
/// </summary>
public sealed class Cartesian3DMapper : ISpatialMapper
{
    private readonly CartesianNormalizer _normalizer;
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;
    private readonly string _xFieldName;
    private readonly string _yFieldName;
    private readonly string _zFieldName;

    public Cartesian3DMapper(
        CartesianNormalizer normalizer,
        ISpatialIndexEncoder encoder,
        string geometryFieldName,
        string xFieldName = "x",
        string yFieldName = "y",
        string zFieldName = "z")
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName))
            : geometryFieldName;
        _xFieldName = string.IsNullOrWhiteSpace(xFieldName) ? "x" : xFieldName;
        _yFieldName = string.IsNullOrWhiteSpace(yFieldName) ? "y" : yFieldName;
        _zFieldName = string.IsNullOrWhiteSpace(zFieldName) ? "z" : zFieldName;
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper requires three-dimensional points.");
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper requires three-dimensional points.");
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        var normalized = _normalizer.NormalizePoint3D(point.X, point.Y, point.Z);
        Span<double> buffer = stackalloc double[3];
        buffer[0] = normalized.x;
        buffer[1] = normalized.y;
        buffer[2] = normalized.z;
        return _encoder.Encode(buffer);
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
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
        if (!TryReadDouble(geometryDoc, _xFieldName, out var x) ||
            !TryReadDouble(geometryDoc, _yFieldName, out var y) ||
            !TryReadDouble(geometryDoc, _zFieldName, out var z))
        {
            point = default;
            return false;
        }

        point = new GeoPoint3D(x, y, z);
        return true;
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
