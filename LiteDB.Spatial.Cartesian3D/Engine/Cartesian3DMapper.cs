extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps three-dimensional Cartesian points to index representations.
/// </summary>
public sealed class Cartesian3DMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    public Cartesian3DMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper expects three-dimensional points.");
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
    }

    public ulong Encode(GeoPoint point)
    {
        throw new NotSupportedException("Cartesian3D mapper expects three-dimensional points.");
    }

    public ulong Encode(GeoPoint3D point)
    {
        Span<double> coordinates = stackalloc double[3];
        coordinates[0] = CartesianIndexing(point.X);
        coordinates[1] = CartesianIndexing(point.Y);
        coordinates[2] = CartesianIndexing(point.Z);
        return _encoder.Encode(coordinates);
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        point = default;
        return false;
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryFieldName, out var value) || value.IsNull)
        {
            point = default;
            return false;
        }

        if (value.IsArray && value.AsArray.Count >= 3 && value.AsArray[0].IsNumber && value.AsArray[1].IsNumber && value.AsArray[2].IsNumber)
        {
            point = new GeoPoint3D(value.AsArray[0].AsDouble, value.AsArray[1].AsDouble, value.AsArray[2].AsDouble);
            return true;
        }

        if (value.IsDocument)
        {
            var doc = value.AsDocument;
            if (TryGet(doc, "x", out var x) && TryGet(doc, "y", out var y) && TryGet(doc, "z", out var z))
            {
                point = new GeoPoint3D(x, y, z);
                return true;
            }
        }

        point = default;
        return false;
    }

    private static bool TryGet(BaseLiteDB.BsonDocument document, string name, out double value)
    {
        if (document.TryGetValue(name, out var bson) && bson.IsNumber)
        {
            value = bson.AsDouble;
            return true;
        }

        value = default;
        return false;
    }

    private static double CartesianIndexing(double value)
    {
#if NET8_0_OR_GREATER
        return Math.Clamp(value, 0d, 1d);
#else
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinates must be finite numbers.");
        }

        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
#endif
    }
}
