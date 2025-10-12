#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Describes the coarse Morton covering generated for a spatial query.
/// </summary>
public sealed class SpatialIndexCovering
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialIndexCovering"/> class.
    /// </summary>
    /// <param name="ranges">The index ranges produced by the encoder.</param>
    /// <param name="coveringCellCount">The number of ranges requested before applying any max-cell reductions.</param>
    /// <param name="usedMaxCoveringCellFallback">Whether the covering exceeded the configured cell budget and required merging.</param>
    public SpatialIndexCovering(
        IReadOnlyList<SpatialIndexRange> ranges,
        int coveringCellCount,
        bool usedMaxCoveringCellFallback)
    {
        Ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        if (coveringCellCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coveringCellCount), "Covering cell counts cannot be negative.");
        }

        CoveringCellCount = coveringCellCount;
        UsedMaxCoveringCellFallback = usedMaxCoveringCellFallback;
    }

    /// <summary>
    /// Gets the index ranges produced by the encoder after applying any range merging.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> Ranges { get; }

    /// <summary>
    /// Gets the number of ranges originally produced before enforcing <c>MaxCoveringCells</c>.
    /// </summary>
    public int CoveringCellCount { get; }

    /// <summary>
    /// Gets a value indicating whether the encoder had to merge ranges because the covering exceeded <c>MaxCoveringCells</c>.
    /// </summary>
    public bool UsedMaxCoveringCellFallback { get; }
}
