#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a closed range of spatial index values.
/// </summary>
public readonly struct SpatialIndexRange : IEquatable<SpatialIndexRange>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialIndexRange"/> struct.
    /// </summary>
    /// <param name="start">The inclusive start of the range.</param>
    /// <param name="end">The inclusive end of the range.</param>
    public SpatialIndexRange(ulong start, ulong end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The end of the range must be greater than or equal to the start.");
        }

        Start = start;
        End = end;
    }

    /// <summary>
    /// Gets the inclusive start of the range.
    /// </summary>
    public ulong Start { get; }

    /// <summary>
    /// Gets the inclusive end of the range.
    /// </summary>
    public ulong End { get; }

    /// <inheritdoc />
    public bool Equals(SpatialIndexRange other)
    {
        return Start == other.Start && End == other.End;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SpatialIndexRange range && Equals(range);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Start.GetHashCode();
            hash = (hash * 397) ^ End.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[{Start}, {End}]";
    }

    public static bool operator ==(SpatialIndexRange left, SpatialIndexRange right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SpatialIndexRange left, SpatialIndexRange right)
    {
        return !left.Equals(right);
    }
}
