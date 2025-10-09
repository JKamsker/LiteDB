#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a GeoJSON polygon composed of an outer ring and optional holes.
/// </summary>
public sealed class GeoPolygon : IEquatable<GeoPolygon>
{
    private readonly ReadOnlyCollection<GeoPoint> _outer;
    private readonly ReadOnlyCollection<IReadOnlyList<GeoPoint>> _holes;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPolygon"/> class.
    /// </summary>
    /// <param name="outer">The closed outer ring describing the polygon boundary.</param>
    /// <param name="holes">Optional closed inner rings describing voids inside the polygon.</param>
    public GeoPolygon(IReadOnlyList<GeoPoint> outer, IReadOnlyList<IReadOnlyList<GeoPoint>>? holes = null)
    {
        if (outer is null)
        {
            throw new ArgumentNullException(nameof(outer));
        }

        _outer = new ReadOnlyCollection<GeoPoint>(ValidateRing(outer, nameof(outer)));

        if (holes is null || holes.Count == 0)
        {
            _holes = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(Array.Empty<IReadOnlyList<GeoPoint>>());
            return;
        }

        var materialized = new List<IReadOnlyList<GeoPoint>>(holes.Count);
        for (var i = 0; i < holes.Count; i++)
        {
            var ring = holes[i];
            var name = $"holes[{i}]";
            var validated = new ReadOnlyCollection<GeoPoint>(ValidateRing(ring, name));
            materialized.Add(validated);
        }

        _holes = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(materialized);
    }

    /// <summary>
    /// Gets the closed outer ring describing the polygon boundary.
    /// </summary>
    public IReadOnlyList<GeoPoint> Outer => _outer;

    /// <summary>
    /// Gets the optional closed rings that describe holes within the polygon.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Holes => _holes;

    /// <inheritdoc />
    public bool Equals(GeoPolygon? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!SequenceEqual(_outer, other._outer))
        {
            return false;
        }

        if (_holes.Count != other._holes.Count)
        {
            return false;
        }

        for (var i = 0; i < _holes.Count; i++)
        {
            if (!SequenceEqual(_holes[i], other._holes[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GeoPolygon polygon && Equals(polygon);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var point in _outer)
            {
                hash = (hash * 397) ^ point.GetHashCode();
            }

            foreach (var hole in _holes)
            {
                foreach (var point in hole)
                {
                    hash = (hash * 397) ^ point.GetHashCode();
                }

                hash = (hash * 397) ^ 31;
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var holesDescription = _holes.Count == 0
            ? "[]"
            : $"[{string.Join("; ", _holes.Select(h => string.Join(", ", h)))}]";
        return $"Polygon(outer: {string.Join(", ", _outer)}, holes: {holesDescription})";
    }

    private static List<GeoPoint> ValidateRing(IReadOnlyList<GeoPoint> ring, string argumentName)
    {
        if (ring is null)
        {
            throw new ArgumentNullException(argumentName);
        }

        if (ring.Count < 4)
        {
            throw new ArgumentException("Polygon rings must contain at least four points including closure.", argumentName);
        }

        var materialized = ring.ToList();
        if (!materialized.First().Equals(materialized.Last()))
        {
            throw new ArgumentException("Polygon rings must be closed (first point equals last point).", argumentName);
        }

        return materialized;
    }

    private static bool SequenceEqual(IReadOnlyList<GeoPoint> left, IReadOnlyList<GeoPoint> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }
}
