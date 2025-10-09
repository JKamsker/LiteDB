#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Encodes normalized coordinates into Morton (Z-order) keys for use as spatial indexes.
/// </summary>
public sealed class MortonIndexEncoder : ISpatialIndexEncoder
{
    private readonly int _dimensions;
    private readonly int _precisionBits;
    private readonly ulong _maxCoordinateValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="MortonIndexEncoder"/> class.
    /// </summary>
    /// <param name="dimensions">Number of spatial dimensions supported by the encoder.</param>
    /// <param name="precisionBits">Number of bits allocated to each axis. Defaults to 32.</param>
    public MortonIndexEncoder(int dimensions, int precisionBits = 32)
    {
        if (dimensions is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Morton encoding supports between one and three dimensions.");
        }

        if (precisionBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be a positive number of bits.");
        }

        if ((long)dimensions * precisionBits > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "The chosen dimensionality and precision exceed the 64-bit Morton key capacity.");
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
            throw new ArgumentException($"Expected {_dimensions} coordinates but received {coordinates.Length}.", nameof(coordinates));
        }

        Span<ulong> quantized = stackalloc ulong[_dimensions];

        for (var i = 0; i < _dimensions; i++)
        {
            quantized[i] = Quantize(coordinates[i]);
        }

        return EncodeQuantized(quantized);
    }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> Cover(BoundingBox bounds, int maxCells)
    {
        if (bounds.Dimensions != _dimensions)
        {
            throw new ArgumentException($"Bounding box dimensionality ({bounds.Dimensions}) does not match encoder dimensionality ({_dimensions}).", nameof(bounds));
        }

        if (maxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCells), "The maximum number of covering cells must be positive.");
        }

        var values = bounds.GetValues();
        var min = new ulong[_dimensions];
        var max = new ulong[_dimensions];

        for (var axis = 0; axis < _dimensions; axis++)
        {
            min[axis] = Quantize(values[axis]);
            max[axis] = Quantize(values[axis + _dimensions]);

            if (max[axis] < min[axis])
            {
                throw new ArgumentException("Bounding box maximum must be greater than or equal to the minimum on every axis.", nameof(bounds));
            }
        }

        var ranges = new List<SpatialIndexRange>();

        var cellSizeShift = DetermineCellShift(min.AsSpan(), max.AsSpan(), maxCells);
        var cellSize = 1UL << cellSizeShift;
        var upperBound = _maxCoordinateValue;

        var cursor = new ulong[_dimensions];
        var cellMax = new ulong[_dimensions];

        void Enumerate(int axis)
        {
            if (axis == _dimensions)
            {
                for (var i = 0; i < _dimensions; i++)
                {
                    var end = cursor[i] + cellSize - 1UL;
                    cellMax[i] = end > upperBound ? upperBound : end;
                }

                var startCode = EncodeQuantized(cursor);
                var endCode = EncodeQuantized(cellMax);
                ranges.Add(new SpatialIndexRange(startCode, endCode));
                return;
            }

            var axisMin = AlignDown(min[axis], cellSize);
            var axisMax = AlignUp(max[axis], cellSize, upperBound);

            for (var value = axisMin; value <= axisMax; value += cellSize)
            {
                cursor[axis] = value;
                Enumerate(axis + 1);
            }
        }

        Enumerate(0);

        return MergeAdjacentRanges(ranges);
    }

    /// <summary>
    /// Coalesces adjacent or overlapping ranges into a minimal set of disjoint ranges.
    /// </summary>
    /// <param name="ranges">The input ranges, which may be unsorted.</param>
    /// <returns>A sorted list of non-overlapping ranges.</returns>
    public static IReadOnlyList<SpatialIndexRange> MergeAdjacentRanges(IEnumerable<SpatialIndexRange> ranges)
    {
        if (ranges is null)
        {
            throw new ArgumentNullException(nameof(ranges));
        }

        var ordered = ranges.OrderBy(static r => r.Start).ThenBy(static r => r.End).ToArray();

        if (ordered.Length == 0)
        {
            return Array.Empty<SpatialIndexRange>();
        }

        var result = new List<SpatialIndexRange>(ordered.Length);
        var current = ordered[0];

        for (var i = 1; i < ordered.Length; i++)
        {
            var next = ordered[i];

            if (next.Start <= current.End + 1)
            {
                var mergedEnd = next.End > current.End ? next.End : current.End;
                current = new SpatialIndexRange(current.Start, mergedEnd);
                continue;
            }

            result.Add(current);
            current = next;
        }

        result.Add(current);
        return result;
    }

    private int DetermineCellShift(ReadOnlySpan<ulong> min, ReadOnlySpan<ulong> max, int maxCells)
    {
        var shift = 0;

        while (shift < _precisionBits)
        {
            var cells = 1UL;

            for (var axis = 0; axis < _dimensions; axis++)
            {
                var span = AlignUp(max[axis], 1UL << shift, _maxCoordinateValue) - AlignDown(min[axis], 1UL << shift) + 1UL;
                var axisCells = span / (1UL << shift);

                if (axisCells == 0)
                {
                    axisCells = 1;
                }

                cells *= axisCells;

                if (cells > (ulong)maxCells)
                {
                    break;
                }
            }

            if (cells <= (ulong)maxCells)
            {
                return shift;
            }

            shift++;
        }

        return _precisionBits;
    }

    private ulong EncodeQuantized(ReadOnlySpan<ulong> coordinates)
    {
        ulong result = 0;

        for (var bit = 0; bit < _precisionBits; bit++)
        {
            for (var axis = 0; axis < _dimensions; axis++)
            {
                var bitValue = (coordinates[axis] >> bit) & 1UL;
                if (bitValue == 0)
                {
                    continue;
                }

                var shift = bit * _dimensions + axis;
                result |= bitValue << shift;
            }
        }

        return result;
    }

    private ulong Quantize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Coordinate values must be finite numbers.", nameof(value));
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

        if (quantized > _maxCoordinateValue)
        {
            quantized = _maxCoordinateValue;
        }

        return quantized;
    }

    private static ulong AlignDown(ulong value, ulong cellSize)
    {
        if (cellSize <= 1)
        {
            return value;
        }

        return (value / cellSize) * cellSize;
    }

    private static ulong AlignUp(ulong value, ulong cellSize, ulong maximum)
    {
        if (cellSize <= 1)
        {
            return value;
        }

        var remainder = value % cellSize;
        if (remainder == cellSize - 1)
        {
            return value;
        }

        var adjusted = value + (cellSize - 1 - remainder);
        return adjusted > maximum ? maximum : adjusted;
    }
}
