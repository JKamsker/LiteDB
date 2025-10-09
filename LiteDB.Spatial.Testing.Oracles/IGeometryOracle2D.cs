namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Provides robust 2D predicate helpers using an external geometry library.
/// </summary>
public interface IGeometryOracle2D
{
    string Name { get; }

    bool IsEnabled { get; }

    GeometryHandle ReadGeometryFromGeoJson(string geoJson);

    bool Contains(GeometryHandle container, Point2D point);

    bool Intersects(GeometryHandle first, GeometryHandle second);

    bool Within(GeometryHandle subject, GeometryHandle container);
}
