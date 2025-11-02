namespace LiteDB.Spatial.Testing.Oracles.Interfaces;

/// <summary>
/// Provides reference implementations for planar geometry predicates.
/// </summary>
public interface IGeometryOracle2D
{
    /// <summary>
    /// Gets a value indicating whether the oracle is available in the current environment.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Loads a geometry from a GeoJSON payload.
    /// </summary>
    /// <param name="geoJson">A GeoJSON geometry or feature payload.</param>
    /// <returns>A geometry handle that can be used with the predicate methods.</returns>
    GeometryHandle LoadFromGeoJson(string geoJson);

    /// <summary>
    /// Creates a polygon from raw coordinate rings.
    /// </summary>
    /// <param name="shell">The outer shell.</param>
    /// <param name="holes">Optional hole definitions.</param>
    /// <returns>A geometry handle.</returns>
    GeometryHandle CreatePolygon(IReadOnlyList<PlanarCoordinate> shell, IReadOnlyList<IReadOnlyList<PlanarCoordinate>>? holes = null);

    /// <summary>
    /// Tests whether the given geometry contains a point.
    /// </summary>
    bool Contains(GeometryHandle geometry, PlanarCoordinate point);

    /// <summary>
    /// Tests whether two geometries intersect.
    /// </summary>
    bool Intersects(GeometryHandle left, GeometryHandle right);

    /// <summary>
    /// Tests whether the <paramref name="inner"/> geometry is fully within the <paramref name="outer"/> geometry.
    /// </summary>
    bool Within(GeometryHandle inner, GeometryHandle outer);
}
