#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a polygon defined by an outer ring and optional interior holes.
/// </summary>
public sealed class GeoPolygon : IEquatable<GeoPolygon>
{
    private static readonly IReadOnlyList<IReadOnlyList<GeoPoint>> EmptyHoles = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(Array.Empty<IReadOnlyList<GeoPoint>>());

    /// <summary>
    /// Initializes a new instance of the <see cref="GeoPolygon"/> class.
    /// </summary>
    /// <param name="outer">The outer ring describing the polygon boundary.</param>
    /// <param name="holes">Optional interior rings that represent holes.</param>
    public GeoPolygon(IReadOnlyList<GeoPoint> outer, IReadOnlyList<IReadOnlyList<GeoPoint>>? holes = null)
    {
        Outer = new ReadOnlyCollection<GeoPoint>(ValidateRing(outer, nameof(outer)));

        if (holes is null || holes.Count == 0)
        {
            Holes = EmptyHoles;
            return;
        }

        var normalized = new List<IReadOnlyList<GeoPoint>>(holes.Count);
        for (var i = 0; i < holes.Count; i++)
        {
            normalized.Add(new ReadOnlyCollection<GeoPoint>(ValidateRing(holes[i], $"holes[{i}]")));
        }

        Holes = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(normalized);
    }

    /// <summary>
    /// Gets the closed ring describing the outer boundary of the polygon.
    /// </summary>
    public IReadOnlyList<GeoPoint> Outer { get; }

    /// <summary>
    /// Gets the optional interior rings representing holes within the polygon.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Holes { get; }

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
            var hash = Outer.Count;
            foreach (var point in Outer)
            {
                hash = (hash * 397) ^ point.GetHashCode();
            }

            foreach (var hole in Holes)
            {
                foreach (var point in hole)
                {
                    hash = (hash * 397) ^ point.GetHashCode();
                }
            }

            return hash;
        }
    }

    private static List<GeoPoint> ValidateRing(IReadOnlyList<GeoPoint> ring, string argumentName)
    {
        if (ring == null)
        {
            throw new ArgumentNullException(argumentName);
        }

        if (ring.Count < 4)
        {
            throw new ArgumentException("Polygon rings must contain at least four points (including closure).", argumentName);
        }

        var copy = ring.ToArray();
        if (!copy[0].Equals(copy[copy.Length - 1]))
        {
            throw new ArgumentException("Polygon rings must be closed (first point equals last point).", argumentName);
        }

        return new List<GeoPoint>(copy);
    }
}
