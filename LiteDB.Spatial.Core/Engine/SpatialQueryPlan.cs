#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a concrete spatial query plan combining index ranges and optional exact filtering metadata.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="dimensions">The dimensionality of the underlying spatial index.</param>
    /// <param name="coveringBounds">Optional coarse bounds applied before precise predicates.</param>
    /// <param name="indexRanges">The ordered set of Morton index ranges to scan.</param>
    /// <param name="exactPredicateDescription">Optional description of the exact predicate used after index filtering.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dimensions"/> is outside the supported 2D/3D range.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="indexRanges"/> is <c>null</c>.</exception>
    public SpatialQueryPlan(
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> indexRanges,
        string? exactPredicateDescription)
    {
        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial query plans must describe either two or three dimensions.");
        }

        IndexRanges = indexRanges ?? throw new ArgumentNullException(nameof(indexRanges));
        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
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
