#nullable enable

using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Describes the pieces required to execute a spatial query using index ranges and precise filters.
/// </summary>
public interface ISpatialQueryPlan
{
    /// <summary>
    /// Gets the name of the spatial engine that produced the plan.
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// Gets the dimensionality of the underlying spatial index.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Gets the coarse bounding region evaluated before exact predicates. May be <c>null</c> when not applicable.
    /// </summary>
    BoundingBox? CoveringBounds { get; }

    /// <summary>
    /// Gets the ordered set of index ranges that should be scanned.
    /// </summary>
    IReadOnlyList<SpatialIndexRange> IndexRanges { get; }

    /// <summary>
    /// Gets the number of index ranges requested before applying the <c>MaxCoveringCells</c> limit.
    /// </summary>
    int CoveringCellCount { get; }

    /// <summary>
    /// Gets a value indicating whether the covering exceeded <c>MaxCoveringCells</c> and required coarse fallback ranges.
    /// </summary>
    bool UsedMaxCoveringCellFallback { get; }

    /// <summary>
    /// Gets a human readable description of the exact predicate applied after index filtering.
    /// </summary>
    string? ExactPredicateDescription { get; }
}
