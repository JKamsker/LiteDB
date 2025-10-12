using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.Features;
using NetTopologySuite.IO;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Thin wrapper around NetTopologySuite for deterministic test assertions.
/// </summary>
public sealed class NtsOracle : IGeometryOracle2D
{
    private readonly GeoJsonReader _reader;
    private readonly bool _enabled;

    public NtsOracle()
    {
        _enabled = OracleEnvironment.Allows("nts");
        _reader = new GeoJsonReader();
    }

    public string Name => "NetTopologySuite";

    public bool IsEnabled => _enabled;

    public GeometryHandle ReadGeometryFromGeoJson(string geoJson)
    {
        EnsureEnabled();

        if (string.IsNullOrWhiteSpace(geoJson))
        {
            throw new ArgumentException("GeoJSON payload cannot be null or empty.", nameof(geoJson));
        }

        var feature = _reader.Read<IFeature?>(geoJson);
        if (feature?.Geometry is Geometry featureGeometry)
        {
            return new GeometryHandle(featureGeometry);
        }

        var geometry = _reader.Read<Geometry?>(geoJson);
        if (geometry is null)
        {
            throw new ArgumentException("GeoJSON payload did not contain a geometry.", nameof(geoJson));
        }

        return new GeometryHandle(geometry);
    }

    public bool Contains(GeometryHandle container, Point2D point)
    {
        EnsureEnabled();
        var ntsGeometry = RequireGeometry(container);
        var ntsPoint = CreatePoint(point);
        return ntsGeometry.Contains(ntsPoint);
    }

    public bool Intersects(GeometryHandle first, GeometryHandle second)
    {
        EnsureEnabled();
        var left = RequireGeometry(first);
        var right = RequireGeometry(second);
        return left.Intersects(right);
    }

    public bool Within(GeometryHandle subject, GeometryHandle container)
    {
        EnsureEnabled();
        var left = RequireGeometry(subject);
        var right = RequireGeometry(container);
        return left.Within(right);
    }

    private static Geometry RequireGeometry(GeometryHandle handle)
    {
        if (handle.Equals(default))
        {
            throw new ArgumentException("Geometry handle was not initialised.", nameof(handle));
        }

        if (handle.Instance is Geometry geometry)
        {
            return geometry;
        }

        throw new ArgumentException("Geometry handle does not originate from the NTS oracle.", nameof(handle));
    }

    private static Point CreatePoint(Point2D point)
    {
        return new Point(point.X, point.Y);
    }

    private void EnsureEnabled()
    {
        if (!_enabled)
        {
            throw new InvalidOperationException("NetTopologySuite oracle is disabled by SPATIAL_ORACLES.");
        }
    }
}
