#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a concrete implementation of <see cref="ISpatialQueryPlan"/>.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="dimensions">The spatial dimensionality handled by the plan.</param>
    /// <param name="coveringBounds">The coarse bounding box applied before exact predicates.</param>
    /// <param name="indexRanges">The ordered set of index ranges to scan.</param>
    /// <param name="exactPredicateDescription">Optional human readable description of the precise predicate.</param>
    public SpatialQueryPlan(
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> indexRanges,
        string? exactPredicateDescription)
    {
        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial query plans must target 2D or 3D indexes.");
        }

        if (indexRanges == null)
        {
            throw new ArgumentNullException(nameof(indexRanges));
        }

        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        IndexRanges = indexRanges.Count == 0
            ? Array.Empty<SpatialIndexRange>()
            : indexRanges;
        ExactPredicateDescription = exactPredicateDescription;
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public BoundingBox? CoveringBounds { get; }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> IndexRanges { get; }

    /// <inheritdoc />
    public string? ExactPredicateDescription { get; }
}
