extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Maps Cartesian 2D points for indexing and persistence.
/// </summary>
public sealed class Cartesian2DMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    public Cartesian2DMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? SpatialCollectionDescriptor.DefaultGeometryFieldName
            : geometryFieldName;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From2D(point.Longitude, point.Latitude, point.Longitude, point.Latitude);
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper does not support 3D points.");
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[2];
        coordinates[0] = CartesianIndexing.Normalize(point.Longitude);
        coordinates[1] = CartesianIndexing.Normalize(point.Latitude);
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        throw new NotSupportedException("Cartesian2D mapper does not support 3D points.");
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
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

        if (value.IsArray && value.AsArray.Count >= 2 && value.AsArray[0].IsNumber && value.AsArray[1].IsNumber)
        {
            point = new GeoPoint(value.AsArray[0].AsDouble, value.AsArray[1].AsDouble);
            return true;
        }

        if (value.IsDocument)
        {
            var doc = value.AsDocument;
            if (TryGet(doc, "x", out var x) && TryGet(doc, "y", out var y))
            {
                point = new GeoPoint(x, y);
                return true;
            }
        }

        point = default;
        return false;
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
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
}
