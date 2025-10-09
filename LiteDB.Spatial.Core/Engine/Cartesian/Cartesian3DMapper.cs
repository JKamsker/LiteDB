extern alias LiteDbBase;

#nullable enable

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial;

internal sealed class Cartesian3DMapper : ISpatialMapper
{
    private readonly ISpatialIndexEncoder _encoder;
    private readonly string _geometryFieldName;

    public Cartesian3DMapper(ISpatialIndexEncoder encoder, string geometryFieldName)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _geometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName)
            ? throw new ArgumentException("Geometry field name must be provided.", nameof(geometryFieldName))
            : geometryFieldName;
    }

    public BoundingBox GetBoundingBox(GeoPoint point)
    {
        return BoundingBox.From3D(point.Longitude, point.Latitude, 0d, point.Longitude, point.Latitude, 0d);
    }

    public BoundingBox GetBoundingBox(GeoPoint3D point)
    {
        return BoundingBox.From3D(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
    }

    public ulong Encode(GeoPoint point)
    {
        Span<double> coordinates = stackalloc double[3];
        coordinates[0] = point.Longitude;
        coordinates[1] = point.Latitude;
        coordinates[2] = 0d;
        return _encoder.Encode(coordinates);
    }

    public ulong Encode(GeoPoint3D point)
    {
        Span<double> coordinates = stackalloc double[3];
        coordinates[0] = point.X;
        coordinates[1] = point.Y;
        coordinates[2] = point.Z;
        return _encoder.Encode(coordinates);
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint point)
    {
        if (!TryReadPoint(document, out GeoPoint3D point3D))
        {
            point = default;
            return false;
        }

        point = new GeoPoint(point3D.X, point3D.Y);
        return true;
    }

    public bool TryReadPoint(BaseLiteDB.BsonDocument document, out GeoPoint3D point)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(_geometryFieldName, out var geometry) || geometry.IsNull)
        {
            point = default;
            return false;
        }

        if (Cartesian2DMapper.TryReadComponents(geometry, out var x, out var y, out var z))
        {
            point = new GeoPoint3D(x, y, z);
            return true;
        }

        throw new SpatialMetadataException($"Geometry field '{_geometryFieldName}' must contain numeric X/Y/Z coordinates.");
    }
}
