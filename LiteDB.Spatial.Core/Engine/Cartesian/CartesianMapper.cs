extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Maps Cartesian coordinates into Morton encoded values.
/// </summary>
public sealed class CartesianMapper : ISpatialMapper
{
    private readonly string _geometryFieldName;
    private readonly ISpatialIndexEncoder _encoder;
    private readonly AxisExtent _xExtent;
    private readonly AxisExtent _yExtent;
    private readonly AxisExtent? _zExtent;

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesianMapper"/> class.
    /// </summary>
    /// <param name="geometryFieldName">Field that stores the coordinate.</param>
    /// <param name="encoder">Index encoder.</param>
    /// <param name="xExtent">Extent for the X axis.</param>
    /// <param name="yExtent">Extent for the Y axis.</param>
    /// <param name="zExtent">Optional Z extent for 3D configurations.</param>
    public CartesianMapper(
        string geometryFieldName,
        ISpatialIndexEncoder encoder,
        AxisExtent xExtent,
        AxisExtent yExtent,
        AxisExtent? zExtent = null)
    {
        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName));
        }

        _geometryFieldName = geometryFieldName;
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _xExtent = xExtent;
        _yExtent = yExtent;
        _zExtent = zExtent;
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    /// <inheritdoc />
    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint point)
    {
        Span<double> values = stackalloc double[2];
        values[0] = _xExtent.Normalize(point.Longitude);
        values[1] = _yExtent.Normalize(point.Latitude);
        return _encoder.Encode(values);
    }

    /// <inheritdoc />
    public ulong Encode(GeoPoint3D point)
    {
        if (_zExtent is null)
        {
            throw new NotSupportedException("The mapper is configured for two-dimensional coordinates.");
        }

        Span<double> values = stackalloc double[3];
        values[0] = _xExtent.Normalize(point.X);
        values[1] = _yExtent.Normalize(point.Y);
        values[2] = _zExtent.Value.Normalize(point.Z);
        return _encoder.Encode(values);
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            return false;
        }

        if (!TryExtractComponents(geometry, out var x, out var y, out var z, expectZ: false))
        {
            return false;
        }

        point = new GeoPoint(x, y);
        return true;
    }

    /// <inheritdoc />
    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        point = default;

        if (_zExtent is null)
        {
            return false;
        }

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            return false;
        }

        if (!TryExtractComponents(geometry, out var x, out var y, out var z, expectZ: true))
        {
            return false;
        }

        point = new GeoPoint3D(x, y, z);
        return true;
    }

    private static bool TryExtractComponents(BaseLiteDB.BsonValue value, out double x, out double y, out double z, bool expectZ)
    {
        x = 0d;
        y = 0d;
        z = 0d;

        if (value.IsDocument)
        {
            var doc = value.AsDocument;

            if (TryReadDouble(doc, "x", out x) && TryReadDouble(doc, "y", out y))
            {
                if (expectZ)
                {
                    return TryReadDouble(doc, "z", out z);
                }

                return true;
            }

            if (doc.TryGetValue("coordinates", out var coords) && coords.IsArray)
            {
                return TryExtractFromArray(coords.AsArray, out x, out y, out z, expectZ);
            }
        }
        else if (value.IsArray)
        {
            return TryExtractFromArray(value.AsArray, out x, out y, out z, expectZ);
        }

        return false;
    }

    private static bool TryExtractFromArray(BaseLiteDB.BsonArray array, out double x, out double y, out double z, bool expectZ)
    {
        x = 0d;
        y = 0d;
        z = 0d;

        var expectedCount = expectZ ? 3 : 2;
        if (array.Count < expectedCount)
        {
            return false;
        }

        if (!TryReadDouble(array[0], out x) || !TryReadDouble(array[1], out y))
        {
            return false;
        }

        if (expectZ)
        {
            return TryReadDouble(array[2], out z);
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

