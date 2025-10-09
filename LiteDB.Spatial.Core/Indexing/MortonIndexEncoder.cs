#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Provides Morton (Z-order) encoding for two and three-dimensional coordinates.
/// </summary>
public sealed class MortonIndexEncoder : ISpatialIndexEncoder
{
    private readonly int _dimensions;
    private readonly int _precisionBits;
    private readonly ulong _maxCoordinateValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="MortonIndexEncoder"/> class.
    /// </summary>
    /// <param name="dimensions">The number of spatial dimensions (2 or 3).</param>
    /// <param name="precisionBits">The number of precision bits per axis.</param>
    public MortonIndexEncoder(int dimensions, int precisionBits)
    {
        if (dimensions != 2 && dimensions != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Morton encoding is defined for 2D or 3D inputs.");
        }

        if (precisionBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be a positive number of bits.");
        }

        if ((long)dimensions * precisionBits > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "The requested precision exceeds the 64-bit key space.");
        }

        _dimensions = dimensions;
        _precisionBits = precisionBits;
        _maxCoordinateValue = (1UL << precisionBits) - 1UL;
    }

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public int PrecisionBits => _precisionBits;

    /// <inheritdoc />
    public ulong Encode(ReadOnlySpan<double> coordinates)
    {
        if (coordinates.Length != _dimensions)
        {
            throw new ArgumentException($"Expected {_dimensions} coordinates, but received {coordinates.Length}.", nameof(coordinates));
        }

        Span<ulong> quantized = stackalloc ulong[3];

        for (var axis = 0; axis < _dimensions; axis++)
        {
            quantized[axis] = Quantize(coordinates[axis]);
        }

        return InterleaveBits(quantized);
    }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> Cover(BoundingBox bounds, int maxCells)
    {
        if (bounds.Dimensions != _dimensions)
        {
            throw new ArgumentException($"The encoder expects a {_dimensions}D bounding box.", nameof(bounds));
        }

        if (maxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCells), "The maximum number of cells must be positive.");
        }

        var min = ArrayPool<ulong>.Shared.Rent(_dimensions);
        var max = ArrayPool<ulong>.Shared.Rent(_dimensions);

        try
        {
            QuantizeBounds(bounds, min, max);

            var ranges = new List<SpatialIndexRange>();
            var stack = new Stack<Node>();
            var initialSpan = 1UL << _precisionBits;

            var root = new Node(0, 0UL, CreateArray(_dimensions, initialSpan, isMin: true), CreateArray(_dimensions, initialSpan, isMin: false), initialSpan);
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                try
                {
                    if (!Intersects(ref node, min, max))
                    {
                        continue;
                    }

                    if (IsContained(ref node, min, max) || node.Level == _precisionBits)
                    {
                        ranges.Add(CreateRange(node.Prefix, node.Level));
                        continue;
                    }

                    var nextLevel = node.Level + 1;
                    var childSpan = node.Span / 2UL;

                    for (var child = (1 << _dimensions) - 1; child >= 0; child--)
                    {
                        var childPrefix = (node.Prefix << _dimensions) | (ulong)(uint)child;
                        var childMin = ArrayPool<ulong>.Shared.Rent(_dimensions);
                        var childMax = ArrayPool<ulong>.Shared.Rent(_dimensions);

                        try
                        {
                            for (var axis = 0; axis < _dimensions; axis++)
                            {
                                var bit = (child >> axis) & 1;
                                var offset = childSpan * (ulong)bit;
                                childMin[axis] = node.Min[axis] + offset;
                                childMax[axis] = childMin[axis] + childSpan - 1UL;
                            }

                            stack.Push(new Node(nextLevel, childPrefix, childMin, childMax, childSpan));
                        }
                        catch
                        {
                            ArrayPool<ulong>.Shared.Return(childMin);
                            ArrayPool<ulong>.Shared.Return(childMax);
                            throw;
                        }
                    }
                }
                finally
                {
                    ArrayPool<ulong>.Shared.Return(node.Min);
                    ArrayPool<ulong>.Shared.Return(node.Max);
                }
            }

            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            var coalesced = CoalesceRanges(ranges);
            return EnforceCellBudget(coalesced, maxCells);
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(min);
            ArrayPool<ulong>.Shared.Return(max);
        }
    }

    private void QuantizeBounds(BoundingBox bounds, Span<ulong> min, Span<ulong> max)
    {
        if (_dimensions == 2)
        {
            min[0] = Quantize(bounds.MinX);
            min[1] = Quantize(bounds.MinY);
            max[0] = Quantize(bounds.MaxX);
            max[1] = Quantize(bounds.MaxY);
        }
        else
        {
            min[0] = Quantize(bounds.MinX);
            min[1] = Quantize(bounds.MinY);
            min[2] = Quantize(bounds.MinZ);
            max[0] = Quantize(bounds.MaxX);
            max[1] = Quantize(bounds.MaxY);
            max[2] = Quantize(bounds.MaxZ);
        }

        for (var i = 0; i < _dimensions; i++)
        {
            if (min[i] > max[i])
            {
                (min[i], max[i]) = (max[i], min[i]);
            }
        }
    }

    private ulong Quantize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Coordinates must be finite numbers.");
        }

        var clamped = value;
        if (clamped < 0d)
        {
            clamped = 0d;
        }
        else if (clamped > 1d)
        {
            clamped = 1d;
        }
        var scaled = clamped * _maxCoordinateValue;
        var quantized = (ulong)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return Math.Min(quantized, _maxCoordinateValue);
    }

    private ulong InterleaveBits(ReadOnlySpan<ulong> values)
    {
        ulong result = 0UL;

        for (var bit = 0; bit < _precisionBits; bit++)
        {
            for (var axis = 0; axis < _dimensions; axis++)
            {
                var bitMask = (values[axis] >> bit) & 1UL;
                var shift = (bit * _dimensions) + axis;
                result |= bitMask << shift;
            }
        }

        return result;
    }

    private static bool Intersects(ref Node node, Span<ulong> min, Span<ulong> max)
    {
        for (var axis = 0; axis < node.Min.Length; axis++)
        {
            if (node.Min[axis] > max[axis] || node.Max[axis] < min[axis])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsContained(ref Node node, Span<ulong> min, Span<ulong> max)
    {
        for (var axis = 0; axis < node.Min.Length; axis++)
        {
            if (node.Min[axis] < min[axis] || node.Max[axis] > max[axis])
            {
                return false;
            }
        }

        return true;
    }

    private SpatialIndexRange CreateRange(ulong prefix, int level)
    {
        var remainingBits = _dimensions * (_precisionBits - level);

        if (remainingBits >= 64)
        {
            return new SpatialIndexRange(0UL, ulong.MaxValue);
        }

        var start = prefix << remainingBits;
        var mask = (1UL << remainingBits) - 1UL;
        var end = start | mask;
        return new SpatialIndexRange(start, end);
    }

    private static List<SpatialIndexRange> CoalesceRanges(List<SpatialIndexRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return ranges;
        }

        var cursor = 0;

        for (var i = 1; i < ranges.Count; i++)
        {
            var current = ranges[cursor];
            var next = ranges[i];

            if (current.CanMerge(next))
            {
                ranges[cursor] = current.Merge(next);
            }
            else
            {
                cursor++;
                ranges[cursor] = next;
            }
        }

        ranges.RemoveRange(cursor + 1, ranges.Count - (cursor + 1));
        return ranges;
    }

    private static IReadOnlyList<SpatialIndexRange> EnforceCellBudget(List<SpatialIndexRange> ranges, int maxCells)
    {
        if (ranges.Count <= maxCells)
        {
            return ranges;
        }

        while (ranges.Count > maxCells)
        {
            var bestIndex = 0;
            ulong smallestGap = ulong.MaxValue;

            for (var i = 0; i < ranges.Count - 1; i++)
            {
                var current = ranges[i];
                var next = ranges[i + 1];
                ulong gap = next.Start > current.End ? next.Start - current.End - 1UL : 0UL;

                if (gap < smallestGap)
                {
                    smallestGap = gap;
                    bestIndex = i;
                }
            }

            var merged = ranges[bestIndex].Merge(ranges[bestIndex + 1]);
            ranges[bestIndex] = merged;
            ranges.RemoveAt(bestIndex + 1);
        }

        return ranges;
    }

    private static ulong[] CreateArray(int dimensions, ulong span, bool isMin)
    {
        var result = ArrayPool<ulong>.Shared.Rent(dimensions);

        for (var i = 0; i < dimensions; i++)
        {
            result[i] = isMin ? 0UL : span - 1UL;
        }

        return result;
    }

    private readonly struct Node
    {
        public Node(int level, ulong prefix, ulong[] min, ulong[] max, ulong span)
        {
            Level = level;
            Prefix = prefix;
            Min = min;
            Max = max;
            Span = span;
        }

        public int Level { get; }

        public ulong Prefix { get; }

        public ulong[] Min { get; }

        public ulong[] Max { get; }

        public ulong Span { get; }
    }
}
