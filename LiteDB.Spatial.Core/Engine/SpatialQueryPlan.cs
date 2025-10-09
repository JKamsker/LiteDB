#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LiteDB.Spatial;

/// <summary>
/// Default implementation of <see cref="ISpatialQueryPlan"/>.
/// </summary>
public sealed class SpatialQueryPlan : ISpatialQueryPlan
{
    private readonly IReadOnlyList<SpatialIndexRange> _indexRanges;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialQueryPlan"/> class.
    /// </summary>
    /// <param name="dimensions">The dimensionality of the underlying spatial index.</param>
    /// <param name="coveringBounds">Optional bounding box describing the coarse search area.</param>
    /// <param name="indexRanges">The index ranges that should be scanned.</param>
    /// <param name="exactPredicateDescription">Human readable description of the precise predicate.</param>
    public SpatialQueryPlan(
        int dimensions,
        BoundingBox? coveringBounds,
        IEnumerable<SpatialIndexRange> indexRanges,
        string? exactPredicateDescription)
    {
        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial plans must target 2D or 3D indexes.");
        }

        if (indexRanges == null)
        {
            throw new ArgumentNullException(nameof(indexRanges));
        }

        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        ExactPredicateDescription = exactPredicateDescription;

        var materialized = new List<SpatialIndexRange>();

        foreach (var range in indexRanges)
        {
            materialized.Add(range);
        }

        _indexRanges = new ReadOnlyCollection<SpatialIndexRange>(materialized);
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
