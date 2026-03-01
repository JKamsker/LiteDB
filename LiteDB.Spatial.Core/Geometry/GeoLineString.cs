#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a two-dimensional line string composed of connected points.
/// </summary>
public sealed class GeoLineString : IEquatable<GeoLineString>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeoLineString"/> class.
    /// </summary>
    /// <param name="points">The ordered set of points forming the line.</param>
    public GeoLineString(IReadOnlyList<GeoPoint> points)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (points.Count < 2)
        {
            throw new ArgumentException("Line strings require at least two points.", nameof(points));
        }

        var copy = points.ToArray();
        Points = new ReadOnlyCollection<GeoPoint>(copy);
    }

    /// <summary>
    /// Gets the ordered set of points that define the line string.
    /// </summary>
    public IReadOnlyList<GeoPoint> Points { get; }

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

        if (Points.Count != other.Points.Count)
        {
            return false;
        }

        for (var i = 0; i < Points.Count; i++)
        {
            if (!Points[i].Equals(other.Points[i]))
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
            var hash = Points.Count;
            for (var i = 0; i < Points.Count; i++)
            {
                hash = (hash * 397) ^ Points[i].GetHashCode();
            }

            return hash;
        }
    }
}
