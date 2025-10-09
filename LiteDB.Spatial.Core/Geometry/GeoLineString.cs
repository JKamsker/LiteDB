#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a sequence of two-dimensional points forming a GeoJSON LineString.
/// </summary>
public sealed class GeoLineString : IEquatable<GeoLineString>
{
    private readonly ReadOnlyCollection<GeoPoint> _points;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeoLineString"/> class.
    /// </summary>
    /// <param name="points">The ordered collection of points that compose the line string.</param>
    public GeoLineString(IReadOnlyList<GeoPoint> points)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (points.Count < 2)
        {
            throw new ArgumentException("LineString requires at least two points.", nameof(points));
        }

        var copy = new GeoPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            copy[i] = points[i];
        }

        _points = Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Gets the ordered points that compose the line string.
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
            for (var i = 0; i < _points.Count; i++)
            {
                hash = (hash * 397) ^ _points[i].GetHashCode();
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"LineString({string.Join(", ", _points.Select(p => p.ToString()))})";
    }
}

