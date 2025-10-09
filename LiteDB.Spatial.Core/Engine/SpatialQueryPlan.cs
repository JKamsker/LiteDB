#nullable enable

using System;
using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Represents an immutable spatial query plan returned by concrete engines.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="dimensions">The dimensionality supported by the underlying engine.</param>
    /// <param name="coveringBounds">Optional normalized bounding box applied before exact filtering.</param>
    /// <param name="indexRanges">The Morton index ranges to scan.</param>
    /// <param name="exactPredicateDescription">Human readable description of the exact predicate.</param>
    public SpatialQueryPlan(
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> indexRanges,
        string? exactPredicateDescription)
    {
        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial query plans must be two or three dimensional.");
        }

        if (indexRanges == null)
        {
            throw new ArgumentNullException(nameof(indexRanges));
        }

        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        IndexRanges = indexRanges;
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

