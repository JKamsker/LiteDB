#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents an ordered set of points describing a GeoJSON LineString geometry.
/// </summary>
public sealed class GeoLineString : IEquatable<GeoLineString>
{
    private readonly ReadOnlyCollection<GeoPoint> _points;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeoLineString"/> class.
    /// </summary>
    /// <param name="points">The ordered points that compose the line.</param>
    public GeoLineString(IEnumerable<GeoPoint> points)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        var materialized = points.ToList();
        if (materialized.Count < 2)
        {
            throw new ArgumentException("LineString requires at least two points.", nameof(points));
        }

        _points = new ReadOnlyCollection<GeoPoint>(materialized);
    }

    /// <summary>
    /// Gets the ordered list of points that compose the line string.
    /// </summary>
    public IReadOnlyList<GeoPoint> Points => _points;

    /// <inheritdoc />
    public bool Equals(GeoLineString? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_points.Count != other._points.Count)
        {
            return false;
        }

        for (var i = 0; i < _points.Count; i++)
        {
            if (!_points[i].Equals(other._points[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GeoLineString line && Equals(line);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var point in _points)
            {
                hash = (hash * 397) ^ point.GetHashCode();
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"LineString({string.Join(", ", _points)})";
    }
}
