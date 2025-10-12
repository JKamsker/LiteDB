#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Captures diagnostic information about the Morton covering requested by a spatial plan.
/// </summary>
public readonly struct SpatialCoveringDiagnostics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCoveringDiagnostics"/> struct.
    /// </summary>
    /// <param name="requestedRangeCount">Number of index ranges requested prior to fallback limits.</param>
    /// <param name="returnedRangeCount">Number of ranges returned by the encoder before any additional union operations.</param>
    /// <param name="estimatedCellCount">Estimated number of Morton cells contained in the requested bounds.</param>
    /// <param name="usedMaxCoveringCellsFallback">Indicates whether the encoder had to merge ranges to honour the max cell cap.</param>
    /// <param name="usedEnumerationFallback">Indicates whether enumeration was skipped in favour of a coarse covering.</param>
    public SpatialCoveringDiagnostics(
        int requestedRangeCount,
        int returnedRangeCount,
        ulong estimatedCellCount,
        bool usedMaxCoveringCellsFallback,
        bool usedEnumerationFallback)
    {
        RequestedRangeCount = requestedRangeCount;
        ReturnedRangeCount = returnedRangeCount;
        EstimatedCellCount = estimatedCellCount;
        UsedMaxCoveringCellsFallback = usedMaxCoveringCellsFallback;
        UsedEnumerationFallback = usedEnumerationFallback;
        EffectiveRangeCount = returnedRangeCount;
    }

    private SpatialCoveringDiagnostics(
        int requestedRangeCount,
        int returnedRangeCount,
        int effectiveRangeCount,
        ulong estimatedCellCount,
        bool usedMaxCoveringCellsFallback,
        bool usedEnumerationFallback)
    {
        RequestedRangeCount = requestedRangeCount;
        ReturnedRangeCount = returnedRangeCount;
        EffectiveRangeCount = effectiveRangeCount;
        EstimatedCellCount = estimatedCellCount;
        UsedMaxCoveringCellsFallback = usedMaxCoveringCellsFallback;
        UsedEnumerationFallback = usedEnumerationFallback;
    }

    /// <summary>
    /// Gets an empty diagnostic instance representing the absence of covering data.
    /// </summary>
    public static SpatialCoveringDiagnostics Empty { get; } = new(0, 0, 0UL, usedMaxCoveringCellsFallback: false, usedEnumerationFallback: false);

    /// <summary>
    /// Gets the number of ranges requested prior to applying fallback limits.
    /// </summary>
    public int RequestedRangeCount { get; }

    /// <summary>
    /// Gets the number of ranges returned by the encoder before any union operations.
    /// </summary>
    public int ReturnedRangeCount { get; }

    /// <summary>
    /// Gets the number of ranges that remain after union operations have been applied.
    /// </summary>
    public int EffectiveRangeCount { get; }

    /// <summary>
    /// Gets the estimated number of Morton cells contained within the requested bounds.
    /// </summary>
    public ulong EstimatedCellCount { get; }

    /// <summary>
    /// Gets a value indicating whether the covering hit the maximum cell fallback.
    /// </summary>
    public bool UsedMaxCoveringCellsFallback { get; }

    /// <summary>
    /// Gets a value indicating whether enumeration was skipped due to large coverings.
    /// </summary>
    public bool UsedEnumerationFallback { get; }

    /// <summary>
    /// Returns a diagnostics instance whose effective range count has been updated to reflect downstream unions.
    /// </summary>
    /// <param name="effectiveRangeCount">The number of ranges that will be scanned after union operations.</param>
    /// <returns>A diagnostics instance with the supplied effective range count.</returns>
    public SpatialCoveringDiagnostics WithEffectiveRangeCount(int effectiveRangeCount)
    {
        return new SpatialCoveringDiagnostics(
            RequestedRangeCount,
            ReturnedRangeCount,
            effectiveRangeCount,
            EstimatedCellCount,
            UsedMaxCoveringCellsFallback,
            UsedEnumerationFallback);
    }
}
