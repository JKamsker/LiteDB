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
    /// <param name="covering">Details about the Morton covering produced for the plan.</param>
    /// <param name="predicateDescription">Human friendly description of the exact predicate.</param>
    public SpatialQueryPlan(
        string engineName,
        int dimensions,
        BoundingBox? coveringBounds,
        SpatialCoveringResult covering,
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
        Covering = covering ?? throw new ArgumentNullException(nameof(covering));
        ExactPredicateDescription = predicateDescription;
    }

    /// <inheritdoc />
    public string EngineName { get; }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public BoundingBox? CoveringBounds { get; }

    /// <inheritdoc />
    public IReadOnlyList<SpatialIndexRange> IndexRanges => Covering.Ranges;

    /// <inheritdoc />
    public string? ExactPredicateDescription { get; }

    /// <summary>
    /// Gets details about the Morton covering used for this plan.
    /// </summary>
    public SpatialCoveringResult Covering { get; }
}
