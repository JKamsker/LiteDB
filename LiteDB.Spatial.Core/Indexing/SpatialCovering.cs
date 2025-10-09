#nullable enable

using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the result of covering a bounding box with Morton index ranges.
/// </summary>
public readonly struct SpatialCovering
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCovering"/> struct.
    /// </summary>
    /// <param name="ranges">The index ranges approximating the covered region.</param>
    /// <param name="cellCountEstimate">Estimated number of discrete Morton cells considered while producing the covering.</param>
    /// <param name="wasClippedByMaxCells">Indicates whether the requested maximum cell count forced ranges to merge.</param>
    public SpatialCovering(
        IReadOnlyList<SpatialIndexRange> ranges,
        ulong cellCountEstimate,
        bool wasClippedByMaxCells)
    {
        Ranges = ranges;
        CellCountEstimate = cellCountEstimate;
        WasClippedByMaxCells = wasClippedByMaxCells;
    }

    /// <summary>
    /// Gets the ranges that approximate the covered region.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> Ranges { get; }

    /// <summary>
    /// Gets the estimated number of Morton cells examined while producing the covering.
    /// </summary>
    public ulong CellCountEstimate { get; }

    /// <summary>
    /// Gets a value indicating whether the covering exceeded the maximum number of cells requested by the caller.
    /// </summary>
    public bool WasClippedByMaxCells { get; }
}
