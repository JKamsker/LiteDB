#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a GeoJSON Polygon constructed from <see cref="GeoPoint"/> rings.
/// </summary>
public sealed class GeoPolygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPolygon"/> class.
    /// </summary>
    /// <param name="outer">The exterior ring. The first and last point must be identical.</param>
    /// <param name="holes">Optional interior rings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outer"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when rings are malformed.</exception>
    public GeoPolygon(IEnumerable<GeoPoint> outer, IEnumerable<IEnumerable<GeoPoint>>? holes = null)
    {
        if (outer == null)
        {
            throw new ArgumentNullException(nameof(outer));
        }

        Outer = new ReadOnlyCollection<GeoPoint>(NormalizeRing(outer, nameof(outer)));

        if (holes == null)
        {
            Holes = Array.Empty<IReadOnlyList<GeoPoint>>();
            return;
        }

        var materialized = new List<IReadOnlyList<GeoPoint>>();
        var index = 0;
        foreach (var ring in holes)
        {
            var normalized = NormalizeRing(ring, $"holes[{index}]");
            materialized.Add(new ReadOnlyCollection<GeoPoint>(normalized));
            index++;
        }

        Holes = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(materialized);
    }

    /// <summary>
    /// Gets the exterior ring of the polygon.
    /// </summary>
    public IReadOnlyList<GeoPoint> Outer { get; }

    /// <summary>
    /// Gets the optional interior rings.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Holes { get; }

    private static List<GeoPoint> NormalizeRing(IEnumerable<GeoPoint> ring, string name)
    {
        if (ring == null)
        {
            throw new ArgumentNullException(name);
        }

        var materialized = ring.ToList();
        if (materialized.Count < 4)
        {
            throw new ArgumentException("Polygon rings must contain at least four points including closure.", name);
        }

        if (!materialized.First().Equals(materialized.Last()))
        {
            throw new ArgumentException("Polygon rings must be closed (first and last point must match).", name);
        }

        return materialized;
    }
}
