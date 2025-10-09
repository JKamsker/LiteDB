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
    public SpatialQueryPlan(
        string engineName,
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> indexRanges,
        string? predicateDescription)
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
    public string? ExactPredicateDescription { get; }
}
