#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a polygon described by an outer ring and optional interior holes.
/// </summary>
public sealed class GeoPolygon : IEquatable<GeoPolygon>
{
    private readonly ReadOnlyCollection<GeoPoint> _outer;
    private readonly ReadOnlyCollection<IReadOnlyList<GeoPoint>> _holes;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPolygon"/> class.
    /// </summary>
    /// <param name="outer">The outer ring expressed as a closed sequence of points.</param>
    /// <param name="holes">Optional inner rings expressed as closed sequences of points.</param>
    public GeoPolygon(IReadOnlyList<GeoPoint> outer, IReadOnlyList<IReadOnlyList<GeoPoint>>? holes = null)
    {
        _outer = ValidateRing(outer, nameof(outer));

        if (holes == null || holes.Count == 0)
        {
            _holes = Array.AsReadOnly(Array.Empty<IReadOnlyList<GeoPoint>>());
            return;
        }

        var normalized = new IReadOnlyList<GeoPoint>[holes.Count];
        for (var i = 0; i < holes.Count; i++)
        {
            normalized[i] = ValidateRing(holes[i], $"{nameof(holes)}[{i}]");
        }

        _holes = Array.AsReadOnly(normalized);
    }

    /// <summary>
    /// Gets the outer ring of the polygon.
    /// </summary>
    public IReadOnlyList<GeoPoint> Outer => _outer;

    /// <summary>
    /// Gets the inner rings that describe holes within the polygon.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Holes => _holes;

    /// <inheritdoc />
    public bool Equals(GeoPolygon? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!Outer.SequenceEqual(other.Outer))
        {
            return false;
        }

        if (Holes.Count != other.Holes.Count)
        {
            return false;
        }

        for (var i = 0; i < Holes.Count; i++)
        {
            if (!Holes[i].SequenceEqual(other.Holes[i]))
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
            for (var i = 0; i < Outer.Count; i++)
            {
                hash = (hash * 397) ^ Outer[i].GetHashCode();
            }

            for (var holeIndex = 0; holeIndex < Holes.Count; holeIndex++)
            {
                var hole = Holes[holeIndex];
                for (var i = 0; i < hole.Count; i++)
                {
                    hash = (hash * 397) ^ hole[i].GetHashCode();
                }
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Polygon(outer={Outer.Count}, holes={Holes.Count})";
    }

    private static ReadOnlyCollection<GeoPoint> ValidateRing(IReadOnlyList<GeoPoint> ring, string argumentName)
    {
        if (ring == null)
        {
            throw new ArgumentNullException(argumentName);
        }

        if (ring.Count < 4)
        {
            throw new ArgumentException("Polygon rings must contain at least four points including closure.", argumentName);
        }

        var copy = new GeoPoint[ring.Count];
        for (var i = 0; i < ring.Count; i++)
        {
            copy[i] = ring[i];
        }

        if (!copy[0].Equals(copy[copy.Length - 1]))
        {
            throw new ArgumentException("Polygon rings must be closed (first and last point must match).", argumentName);
        }

        return Array.AsReadOnly(copy);
    }
}

