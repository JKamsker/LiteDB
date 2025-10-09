#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Captures diagnostic metrics describing how a spatial covering was produced.
/// </summary>
public readonly struct SpatialCoveringMetrics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCoveringMetrics"/> struct.
    /// </summary>
    /// <param name="cellCountEstimate">Estimated number of cells considered while constructing the covering.</param>
    /// <param name="requestedMaxCoveringCells">The maximum number of covering cells requested by the caller.</param>
    /// <param name="rangeCount">The number of ranges emitted by the planner after any post-processing.</param>
    /// <param name="wasClippedByMaxCells">Indicates whether the covering exceeded the requested maximum and had to be merged.</param>
    public SpatialCoveringMetrics(
        ulong cellCountEstimate,
        int requestedMaxCoveringCells,
        int rangeCount,
        bool wasClippedByMaxCells)
    {
        CellCountEstimate = cellCountEstimate;
        RequestedMaxCoveringCells = requestedMaxCoveringCells;
        RangeCount = rangeCount;
        WasClippedByMaxCells = wasClippedByMaxCells;
    }

    /// <summary>
    /// Gets the estimated number of Morton cells that participate in the covering.
    /// </summary>
    public ulong CellCountEstimate { get; }

    /// <summary>
    /// Gets the configured maximum number of covering cells.
    /// </summary>
    public int RequestedMaxCoveringCells { get; }

    /// <summary>
    /// Gets the number of index ranges emitted after post-processing.
    /// </summary>
    public int RangeCount { get; }

    /// <summary>
    /// Gets a value indicating whether the covering exceeded the requested maximum cell budget.
    /// </summary>
    public bool WasClippedByMaxCells { get; }

    /// <summary>
    /// Gets an empty metrics instance used when a covering is not applicable.
    /// </summary>
    public static SpatialCoveringMetrics Empty { get; } = new SpatialCoveringMetrics(0, 0, 0, wasClippedByMaxCells: false);
}
