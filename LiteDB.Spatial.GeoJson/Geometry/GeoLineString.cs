#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a GeoJSON LineString backed by <see cref="GeoPoint"/> coordinates.
/// </summary>
public sealed class GeoLineString
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeoLineString"/> class.
    /// </summary>
    /// <param name="points">The ordered set of points describing the line string.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="points"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when fewer than two points are supplied.</exception>
    public GeoLineString(IEnumerable<GeoPoint> points)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        var materialized = points.ToList();
        if (materialized.Count < 2)
        {
            throw new ArgumentException("LineString requires at least two points.", nameof(points));
        }

        Points = new ReadOnlyCollection<GeoPoint>(materialized);
    }

    /// <summary>
    /// Gets the ordered set of points that compose the line string.
    /// </summary>
    public IReadOnlyList<GeoPoint> Points { get; }
}
