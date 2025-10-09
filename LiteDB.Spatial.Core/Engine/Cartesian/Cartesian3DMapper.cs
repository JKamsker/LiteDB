#nullable enable

extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps three-dimensional Cartesian points for indexing.
/// </summary>
public sealed class Cartesian3DMapper : ISpatialMapper
{
    private readonly string _geometryFieldName;
    private readonly ISpatialIndexEncoder _encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cartesian3DMapper"/> class.
    /// </summary>
    public Cartesian3DMapper(string geometryFieldName, ISpatialIndexEncoder encoder)
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
        Span<double> coordinates = stackalloc double[3];
        coordinates[0] = point.X;
        coordinates[1] = point.Y;
        coordinates[2] = point.Z;
        return _encoder.Encode(coordinates);
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
        point = default;

        if (!document.TryGetValue(_geometryFieldName, out var value) || value.IsNull)
        {
            return false;
        }

        if (value.IsDocument)
        {
            var doc = value.AsDocument;
            if (TryReadCoordinateTriple(doc, out var x, out var y, out var z))
            {
                point = new GeoPoint3D(x, y, z);
                return true;
            }

            if (doc.TryGetValue("coordinates", out var arrayValue) && TryReadFromArray(arrayValue, out x, out y, out z))
            {
                point = new GeoPoint3D(x, y, z);
                return true;
            }
        }
        else if (TryReadFromArray(value, out var xValue, out var yValue, out var zValue))
        {
            point = new GeoPoint3D(xValue, yValue, zValue);
            return true;
        }

        return false;
    }

    private static bool TryReadCoordinateTriple(BaseLiteDB.BsonDocument document, out double x, out double y, out double z)
    {
        if (document.TryGetValue("x", out var xValue) && document.TryGetValue("y", out var yValue)
            && document.TryGetValue("z", out var zValue)
            && xValue.IsNumber && yValue.IsNumber && zValue.IsNumber)
        {
            x = xValue.AsDouble;
            y = yValue.AsDouble;
            z = zValue.AsDouble;
            return true;
        }

        x = 0d;
        y = 0d;
        z = 0d;
        return false;
    }

    private static bool TryReadFromArray(BaseLiteDB.BsonValue value, out double x, out double y, out double z)
    {
        if (!value.IsArray)
        {
            x = 0d;
            y = 0d;
            z = 0d;
            return false;
        }

        var array = value.AsArray;
        if (array.Count < 3 || !array[0].IsNumber || !array[1].IsNumber || !array[2].IsNumber)
        {
            x = 0d;
            y = 0d;
            z = 0d;
            return false;
        }

        x = array[0].AsDouble;
        y = array[1].AsDouble;
        z = array[2].AsDouble;
        return true;
    }
}
