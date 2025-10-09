using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Implements a Morton (Z-order) index encoder for two- and three-dimensional coordinates.
/// </summary>
public sealed class MortonIndexEncoder : ISpatialIndexEncoder
{
    private readonly ulong _maxValue;
    private readonly ulong _enumerationThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="MortonIndexEncoder"/> class.
    /// </summary>
    /// <param name="dimensions">The dimensionality supported by the encoder. Only 2D and 3D are supported.</param>
    /// <param name="precisionBits">The number of bits allocated to each coordinate axis.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dimensions"/> or <paramref name="precisionBits"/> are out of range.</exception>
    public MortonIndexEncoder(int dimensions, int precisionBits)
    {
        if (dimensions != 2 && dimensions != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Morton encoding currently supports only two or three dimensions.");
        }

        if (precisionBits <= 0 || precisionBits > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision bits must be between 1 and 32.");
        }

        Dimensions = dimensions;
        PrecisionBits = precisionBits;
        _maxValue = (1UL << precisionBits) - 1UL;
        _enumerationThreshold = Math.Max(1UL, (ulong)precisionBits * 32UL);
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public int PrecisionBits { get; }

    /// <inheritdoc />
    public ulong Encode(ReadOnlySpan<double> coordinates)
    {
        if (coordinates.Length != Dimensions)
        {
            throw new ArgumentException($"Expected {Dimensions} coordinates but received {coordinates.Length}.", nameof(coordinates));
        }

        Span<ulong> quantized = stackalloc ulong[Dimensions];

        for (var axis = 0; axis < Dimensions; axis++)
        {
            quantized[axis] = Quantize(coordinates[axis]);
        }

        return InterleaveBits(quantized);
    }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> Cover(BoundingBox bounds, int maxCells)
    {
        if (bounds.Dimensions != Dimensions)
        {
            throw new ArgumentException($"Expected a {Dimensions}D bounding box but received {bounds.Dimensions}D.", nameof(bounds));
        }

        if (maxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCells), "The maximum number of covering cells must be positive.");
        }

        var minimum = QuantizeBounds(bounds, isMax: false);
        var maximum = QuantizeBounds(bounds, isMax: true);

        var totalCells = EstimateCellCount(minimum, maximum);

        if (totalCells == 0)
        {
            return Array.Empty<SpatialIndexRange>();
        }

        if (totalCells > _enumerationThreshold)
        {
            var start = InterleaveBits(minimum);
            var end = InterleaveBits(maximum);
            if (start > end)
            {
                (start, end) = (end, start);
            }

            return new[] { new SpatialIndexRange(start, end) };
        }

        var buffer = new List<ulong>((int)Math.Min(totalCells, int.MaxValue));
        EnumerateCodes(minimum, maximum, buffer, new ulong[Dimensions], 0);
        buffer.Sort();

        var ranges = BuildRanges(buffer);
        return ReduceRangeCount(ranges, maxCells);
    }

    /// <summary>
    /// Merges adjacent and overlapping ranges to create a simplified covering.
    /// </summary>
    /// <param name="ranges">The ranges to simplify.</param>
    /// <returns>A collection of merged ranges ordered by their starting value.</returns>
    public static IReadOnlyList<SpatialIndexRange> UnionAdjacentRanges(IEnumerable<SpatialIndexRange> ranges)
    {
        if (ranges == null)
        {
            throw new ArgumentNullException(nameof(ranges));
        }

        var ordered = ranges.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        if (ordered.Count == 0)
        {
            return ordered;
        }

        var result = new List<SpatialIndexRange>(ordered.Count);
        var current = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var candidate = ordered[i];
            if (candidate.Start <= current.End + 1)
            {
                current = new SpatialIndexRange(current.Start, Math.Max(current.End, candidate.End));
            }
            else
            {
                result.Add(current);
                current = candidate;
            }
        }

        result.Add(current);
        return result;
    }

    private ulong Quantize(double coordinate)
    {
        if (double.IsNaN(coordinate) || double.IsInfinity(coordinate))
        {
            throw new ArgumentException("Coordinates must be finite values.");
        }

#if NET8_0_OR_GREATER
        var clamped = Math.Clamp(coordinate, 0d, 1d);
#else
        var clamped = Clamp01(coordinate);
#endif
        
        var scaled = clamped * _maxValue;
        var quantized = (ulong)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return quantized > _maxValue ? _maxValue : quantized;
    }

    private ulong[] QuantizeBounds(BoundingBox bounds, bool isMax)
    {
        var values = bounds.GetValues();
        var buffer = new ulong[Dimensions];
        for (var axis = 0; axis < Dimensions; axis++)
        {
            var offset = isMax ? axis + Dimensions : axis;
            buffer[axis] = Quantize(values[offset]);
        }

        return buffer;
    }

    private static double Clamp01(double value)
    {
        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
    }

    private static ulong EstimateCellCount(IReadOnlyList<ulong> min, IReadOnlyList<ulong> max)
    {
        try
        {
            ulong total = 1;
            for (var i = 0; i < min.Count; i++)
            {
                total = checked(total * (max[i] - min[i] + 1));
            }

            return total;
        }
        catch (OverflowException)
        {
            return ulong.MaxValue;
        }
    }

    private void EnumerateCodes(ulong[] minimum, ulong[] maximum, List<ulong> buffer, ulong[] current, int axis)
    {
        if (axis == Dimensions)
        {
            buffer.Add(InterleaveBits(current));
            return;
        }

        for (var value = minimum[axis]; value <= maximum[axis]; value++)
        {
            current[axis] = value;
            EnumerateCodes(minimum, maximum, buffer, current, axis + 1);
        }
    }

    private IReadOnlyList<SpatialIndexRange> ReduceRangeCount(List<SpatialIndexRange> ranges, int maxCells)
    {
        if (ranges.Count <= maxCells)
        {
            return ranges;
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));

        while (ranges.Count > maxCells && ranges.Count > 1)
        {
            var bestIndex = 0;
            ulong bestCost = ulong.MaxValue;

            for (var i = 0; i < ranges.Count - 1; i++)
            {
                var current = ranges[i];
                var next = ranges[i + 1];
                var cost = next.End - current.Start;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIndex = i;
                }
            }

            var merged = new SpatialIndexRange(ranges[bestIndex].Start, ranges[bestIndex + 1].End);
            ranges[bestIndex] = merged;
            ranges.RemoveAt(bestIndex + 1);
        }

        return ranges;
    }

    private static List<SpatialIndexRange> BuildRanges(List<ulong> codes)
    {
        if (codes.Count == 0)
        {
            return new List<SpatialIndexRange>();
        }

        var ranges = new List<SpatialIndexRange>();
        ulong start = codes[0];
        ulong previous = start;

        for (var i = 1; i < codes.Count; i++)
        {
            var value = codes[i];
            if (value == previous + 1)
            {
                previous = value;
                continue;
            }

            ranges.Add(new SpatialIndexRange(start, previous));
            start = previous = value;
        }

        ranges.Add(new SpatialIndexRange(start, previous));
        return ranges;
    }

    private ulong InterleaveBits(ReadOnlySpan<ulong> values)
    {
        ulong result = 0;
        var bitPosition = 0;

        for (var bit = 0; bit < PrecisionBits; bit++)
        {
            for (var axis = 0; axis < Dimensions; axis++)
            {
                var bitValue = (values[axis] >> bit) & 1UL;
                result |= bitValue << bitPosition;
                bitPosition++;
            }
        }

        return result;
    }
}
