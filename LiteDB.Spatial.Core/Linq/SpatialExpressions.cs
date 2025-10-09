using System;

namespace LiteDB.Spatial;

/// <summary>
/// Provides LINQ-friendly spatial helpers. These methods are intended to be used within expression trees
/// and will be translated by <see cref="SpatialResolver"/> into engine-specific query plans.
/// </summary>
public static class SpatialExpressions
{
    /// <summary>
    /// Placeholder for a query that selects points near a geographic center.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always thrown when invoked directly.</exception>
    public static bool Near(GeoPoint candidate, GeoPoint center, double radius)
    {
        throw CreateUsageException();
    }

    /// <summary>
    /// Placeholder for a query that selects points near a three-dimensional center.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always thrown when invoked directly.</exception>
    public static bool Near(GeoPoint3D candidate, GeoPoint3D center, double radius)
    {
        throw CreateUsageException();
    }

    /// <summary>
    /// Placeholder for a query that selects points contained within a bounding box.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always thrown when invoked directly.</exception>
    public static bool InBox(GeoPoint candidate, BoundingBox bounds)
    {
        throw CreateUsageException();
    }

    /// <summary>
    /// Placeholder for a query that selects three-dimensional points contained within a bounding box.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always thrown when invoked directly.</exception>
    public static bool InBox(GeoPoint3D candidate, BoundingBox bounds)
    {
        throw CreateUsageException();
    }

    private static InvalidOperationException CreateUsageException()
    {
        return new InvalidOperationException("SpatialExpressions are intended for use within LINQ expressions and should not be executed directly.");
    }
}
