#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LiteDB.Spatial;

/// <summary>
/// Represents an immutable implementation of <see cref="ISpatialQueryPlan"/>.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    private readonly IReadOnlyList<SpatialIndexRange> _ranges;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="dimensions">The spatial dimensionality of the plan.</param>
    /// <param name="coveringBounds">The coarse bounding box applied before exact filtering.</param>
    /// <param name="ranges">The index ranges used to service the query.</param>
    /// <param name="exactPredicateDescription">Optional description of the exact predicate used after index pruning.</param>
    public SpatialQueryPlan(
        int dimensions,
        BoundingBox? coveringBounds,
        IEnumerable<SpatialIndexRange> ranges,
        string? exactPredicateDescription)
    {
        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial query plans must be two- or three-dimensional.");
        }

        if (ranges == null)
        {
            throw new ArgumentNullException(nameof(ranges));
        }

        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        _ranges = new ReadOnlyCollection<SpatialIndexRange>(new List<SpatialIndexRange>(ranges));
        ExactPredicateDescription = exactPredicateDescription;
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public BoundingBox? CoveringBounds { get; }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> IndexRanges => _ranges;

    /// <inheritdoc />
    public string? ExactPredicateDescription { get; }
}
