#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the outcome of computing a Morton covering for a bounding box.
/// </summary>
public sealed class SpatialCoveringResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCoveringResult"/> class.
    /// </summary>
    /// <param name="ranges">The ranges that cover the query shape.</param>
    /// <param name="estimatedCellCount">An estimate of the number of discrete cells required to cover the shape.</param>
    /// <param name="requestedMaxCells">The caller supplied limit on covering cells.</param>
    /// <param name="originalRangeCount">The range count prior to any capping or merging.</param>
    public SpatialCoveringResult(
        IReadOnlyList<SpatialIndexRange> ranges,
        ulong estimatedCellCount,
        int requestedMaxCells,
        int originalRangeCount)
    {
        if (ranges == null)
        {
            throw new ArgumentNullException(nameof(ranges));
        }

        if (requestedMaxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedMaxCells), "Requested max cells must be positive.");
        }

        if (originalRangeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalRangeCount), "Original range count cannot be negative.");
        }

        Ranges = ranges;
        EstimatedCellCount = estimatedCellCount;
        RequestedMaxCells = requestedMaxCells;
        OriginalRangeCount = originalRangeCount;
    }

    /// <summary>
    /// Gets the index ranges that cover the requested shape.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> Ranges { get; }

    /// <summary>
    /// Gets the estimated number of discrete Morton cells needed to cover the shape.
    /// </summary>
    public ulong EstimatedCellCount { get; }

    /// <summary>
    /// Gets the caller supplied maximum number of covering cells.
    /// </summary>
    public int RequestedMaxCells { get; }

    /// <summary>
    /// Gets the number of ranges produced before reducing them to honor <see cref="RequestedMaxCells"/>.
    /// </summary>
    public int OriginalRangeCount { get; }

    /// <summary>
    /// Gets the number of ranges returned to the caller after any consolidation.
    /// </summary>
    public int FinalRangeCount => Ranges.Count;

    /// <summary>
    /// Gets a value indicating whether the covering was capped because it exceeded <see cref="RequestedMaxCells"/>.
    /// </summary>
    public bool WasCapped => OriginalRangeCount > RequestedMaxCells;

    /// <summary>
    /// Creates a new covering result that reuses the same metrics but exposes a different range set.
    /// </summary>
    /// <param name="ranges">The replacement ranges.</param>
    /// <returns>A covering result with the supplied ranges.</returns>
    public SpatialCoveringResult WithRanges(IReadOnlyList<SpatialIndexRange> ranges)
    {
        return new SpatialCoveringResult(
            ranges ?? throw new ArgumentNullException(nameof(ranges)),
            EstimatedCellCount,
            RequestedMaxCells,
            OriginalRangeCount);
    }
}
