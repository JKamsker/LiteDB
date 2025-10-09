#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Provides Morton (Z-order) encoding for two or three dimensional coordinates.
/// </summary>
public sealed class MortonIndexEncoder : ISpatialIndexEncoder
{
    private readonly ulong _maxCoordinateValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="MortonIndexEncoder"/> class.
    /// </summary>
    /// <param name="dimensions">The spatial dimensionality supported by the encoder. Only 2D and 3D encoders are currently supported.</param>
    /// <param name="precisionBits">The number of bits assigned to each coordinate axis.</param>
    public MortonIndexEncoder(int dimensions, int precisionBits)
    {
        if (dimensions != 2 && dimensions != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Morton encoding currently supports only two or three dimensions.");
        }

        if (precisionBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be a positive number of bits.");
        }

        if ((long)dimensions * precisionBits > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), "The product of dimensions and precision cannot exceed 64 bits when encoding into a UInt64.");
        }

        Dimensions = dimensions;
        PrecisionBits = precisionBits;
        _maxCoordinateValue = (1UL << precisionBits) - 1UL;
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
            throw new ArgumentException("Coordinate count must match the encoder dimensionality.", nameof(coordinates));
        }

        Span<ulong> quantized = stackalloc ulong[Dimensions];

        for (var i = 0; i < Dimensions; i++)
        {
            quantized[i] = Quantize(coordinates[i]);
        }

        return Interleave(quantized);
    }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> Cover(BoundingBox bounds, int maxCells)
    {
        if (bounds.Dimensions != Dimensions)
        {
            throw new ArgumentException("Bounding box dimensionality must match the encoder dimensionality.", nameof(bounds));
        }

        if (maxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCells), "Maximum covering cells must be positive.");
        }

        Span<ulong> minIndices = stackalloc ulong[Dimensions];
        Span<ulong> maxIndices = stackalloc ulong[Dimensions];

        PopulateAxisIndices(bounds, minIndices, maxIndices);

        var ranges = ComputeCoveringRanges(minIndices, maxIndices, maxCells);

        return ranges.Count switch
        {
            0 => Array.Empty<SpatialIndexRange>(),
            1 => ranges,
            _ => CoalesceRanges(ranges)
        };
    }

    /// <summary>
    /// Combines overlapping or adjacent index ranges into the minimal equivalent covering.
    /// </summary>
    /// <param name="ranges">The ranges to coalesce.</param>
    /// <returns>A new list containing the merged ranges ordered by start value.</returns>
    public static IReadOnlyList<SpatialIndexRange> CoalesceRanges(IEnumerable<SpatialIndexRange> ranges)
    {
        if (ranges is null)
        {
            throw new ArgumentNullException(nameof(ranges));
        }

        var ordered = ranges.ToList();

        if (ordered.Count == 0)
        {
            return Array.Empty<SpatialIndexRange>();
        }

        ordered.Sort((a, b) => a.Start.CompareTo(b.Start));

        var result = new List<SpatialIndexRange>(ordered.Count);
        var current = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var candidate = ordered[i];

            if (candidate.Start <= current.End + 1)
            {
                var mergedEnd = candidate.End > current.End ? candidate.End : current.End;
                current = new SpatialIndexRange(current.Start, mergedEnd);
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
            throw new ArgumentException("Coordinates must be finite numbers.", nameof(coordinate));
        }

        var clamped = coordinate;

        if (clamped < 0d)
        {
            clamped = 0d;
        }
        else if (clamped > 1d)
        {
            clamped = 1d;
        }

        var scaled = Math.Round(clamped * _maxCoordinateValue, MidpointRounding.AwayFromZero);
        var quantized = (ulong)scaled;

        if (quantized > _maxCoordinateValue)
        {
            quantized = _maxCoordinateValue;
        }

        return quantized;
    }

    private void PopulateAxisIndices(BoundingBox bounds, Span<ulong> minIndices, Span<ulong> maxIndices)
    {
        minIndices[0] = Quantize(bounds.MinX);
        maxIndices[0] = Quantize(bounds.MaxX);
        minIndices[1] = Quantize(bounds.MinY);
        maxIndices[1] = Quantize(bounds.MaxY);

        if (Dimensions == 3)
        {
            minIndices[2] = Quantize(bounds.MinZ);
            maxIndices[2] = Quantize(bounds.MaxZ);
        }
    }

    private List<SpatialIndexRange> ComputeCoveringRanges(ReadOnlySpan<ulong> minIndices, ReadOnlySpan<ulong> maxIndices, int maxCells)
    {
        var ranges = new List<SpatialIndexRange>();

        var shift = DetermineCoverShift(minIndices, maxIndices, maxCells);
        var cellSize = 1UL << shift;

        Span<ulong> axisStart = stackalloc ulong[Dimensions];
        Span<ulong> axisEnd = stackalloc ulong[Dimensions];
        Span<ulong> cellIndices = stackalloc ulong[Dimensions];
        Span<ulong> baseIndices = stackalloc ulong[Dimensions];

        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            axisStart[dimension] = minIndices[dimension] >> shift;
            axisEnd[dimension] = maxIndices[dimension] >> shift;
            cellIndices[dimension] = axisStart[dimension];
        }

        while (true)
        {
            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                baseIndices[dimension] = cellIndices[dimension] << shift;
            }

            var start = Interleave(baseIndices);
            var trailingBits = Dimensions * shift;
            ulong mask;

            if (trailingBits >= 64)
            {
                mask = ulong.MaxValue;
            }
            else if (shift == 0)
            {
                mask = 0UL;
            }
            else
            {
                mask = (1UL << trailingBits) - 1UL;
            }

            var end = start | mask;
            ranges.Add(new SpatialIndexRange(start, end));

            var advanced = false;

            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                if (cellIndices[dimension] < axisEnd[dimension])
                {
                    cellIndices[dimension]++;

                    for (var reset = 0; reset < dimension; reset++)
                    {
                        cellIndices[reset] = axisStart[reset];
                    }

                    advanced = true;
                    break;
                }
            }

            if (!advanced)
            {
                break;
            }
        }

        return ranges;
    }

    private int DetermineCoverShift(ReadOnlySpan<ulong> minIndices, ReadOnlySpan<ulong> maxIndices, int maxCells)
    {
        var maxAllowedCells = Math.Max(1, maxCells);

        for (var shift = 0; shift <= PrecisionBits; shift++)
        {
            var cellSize = 1UL << shift;
            long totalCells = 1;
            var withinLimit = true;

            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                var span = maxIndices[dimension] - minIndices[dimension] + 1UL;
                var axisCells = (span + cellSize - 1UL) / cellSize;

                try
                {
                    totalCells = checked(totalCells * (long)axisCells);
                }
                catch (OverflowException)
                {
                    withinLimit = false;
                    break;
                }

                if (totalCells > maxAllowedCells)
                {
                    withinLimit = false;
                    break;
                }
            }

            if (withinLimit)
            {
                return shift;
            }
        }

        return PrecisionBits;
    }

    private ulong Interleave(ReadOnlySpan<ulong> coordinates)
    {
        ulong result = 0UL;

        for (var bit = 0; bit < PrecisionBits; bit++)
        {
            var baseShift = bit * Dimensions;

            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                var bitValue = (coordinates[dimension] >> bit) & 1UL;
                result |= bitValue << (baseShift + dimension);
            }
        }

        return result;
    }
}
