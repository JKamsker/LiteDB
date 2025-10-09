#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Default implementation of <see cref="ISpatialQueryPlan"/> describing the
/// ranges, bounding information, and predicates required to service a spatial query.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    private readonly IReadOnlyList<SpatialIndexRange> _indexRanges;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="dimensions">Dimensionality of the underlying spatial index.</param>
    /// <param name="coveringBounds">Optional bounding region evaluated prior to exact predicates.</param>
    /// <param name="indexRanges">The ordered set of index ranges to scan.</param>
    /// <param name="exactPredicateDescription">Human-readable description of the exact predicate applied after index filtering.</param>
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

        if (indexRanges == null)
        {
            throw new ArgumentNullException(nameof(indexRanges));
        }

        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        _indexRanges = indexRanges.Count == 0 ? Array.Empty<SpatialIndexRange>() : indexRanges.ToArray();
        ExactPredicateDescription = exactPredicateDescription;
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public BoundingBox? CoveringBounds { get; }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> IndexRanges => _indexRanges;

    /// <inheritdoc />
    public string? ExactPredicateDescription { get; }
}
