#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Default implementation of <see cref="ISpatialQueryPlan"/>.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="engineName">The name of the spatial engine that produced the plan.</param>
    /// <param name="dimensions">The dimensionality of the spatial index.</param>
    /// <param name="coveringBounds">The optional coarse covering bounds.</param>
    /// <param name="indexRanges">The index ranges that should be scanned.</param>
    /// <param name="predicateDescription">Human friendly description of the exact predicate.</param>
    /// <param name="coveringCellCount">The number of ranges requested before enforcing the maximum covering cell limit.</param>
    /// <param name="usedMaxCoveringCellFallback">Indicates whether the covering exceeded the configured cell budget.</param>
    public SpatialQueryPlan(
        string engineName,
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> indexRanges,
        string? predicateDescription,
        int coveringCellCount,
        bool usedMaxCoveringCellFallback)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name must be provided.", nameof(engineName));
        }

        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial plans must describe either two or three dimensions.");
        }

        EngineName = engineName;
        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        IndexRanges = indexRanges ?? throw new ArgumentNullException(nameof(indexRanges));
        ExactPredicateDescription = predicateDescription;
        if (coveringCellCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coveringCellCount), "Covering cell count cannot be negative.");
        }

        CoveringCellCount = coveringCellCount;
        UsedMaxCoveringCellFallback = usedMaxCoveringCellFallback;
    }

    /// <inheritdoc />
    public string EngineName { get; }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public BoundingBox? CoveringBounds { get; }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> IndexRanges { get; }

    /// <inheritdoc />
    public int CoveringCellCount { get; }

    /// <inheritdoc />
    public bool UsedMaxCoveringCellFallback { get; }

    /// <inheritdoc />
    public string? ExactPredicateDescription { get; }
}
