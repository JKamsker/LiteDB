#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a human-readable summary of a spatial query plan.
/// </summary>
public sealed class SpatialExplainResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialExplainResult"/> class.
    /// </summary>
    /// <param name="collectionName">The name of the collection the plan targets.</param>
    /// <param name="engineName">The engine that produced the plan.</param>
    /// <param name="dimensions">The dimensionality of the spatial index.</param>
    /// <param name="coveringBounds">Optional coarse covering bounds.</param>
    /// <param name="indexRanges">The index ranges scanned by the plan.</param>
    /// <param name="predicate">Human readable description of the exact predicate.</param>
    public SpatialExplainResult(
        string collectionName,
        string engineName,
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> indexRanges,
        string? predicate)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name must be provided.", nameof(engineName));
        }

        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial plans must describe two or three dimensions.");
        }

        CollectionName = collectionName;
        EngineName = engineName;
        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        IndexRanges = new ReadOnlyCollection<SpatialIndexRange>((indexRanges ?? throw new ArgumentNullException(nameof(indexRanges))).ToList());
        ExactPredicateDescription = predicate;
    }

    /// <summary>
    /// Gets the name of the collection targeted by the plan.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the name of the engine that produced the plan.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the dimensionality of the spatial index.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the optional coarse covering bounds.
    /// </summary>
    public BoundingBox? CoveringBounds { get; }

    /// <summary>
    /// Gets the index ranges scanned by the plan.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> IndexRanges { get; }

    /// <summary>
    /// Gets the human-readable description of the exact predicate.
    /// </summary>
    public string? ExactPredicateDescription { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Collection: {0}", CollectionName));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Engine: {0} ({1}D)", EngineName, Dimensions));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Covering bounds: {0}", CoveringBounds?.ToString() ?? "<none>"));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Index ranges ({0}):", IndexRanges.Count));

        foreach (var range in IndexRanges)
        {
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - {0}", range));
        }

        builder.Append(string.Format(CultureInfo.InvariantCulture, "Exact predicate: {0}", ExactPredicateDescription ?? "<none>"));
        return builder.ToString();
    }
}
