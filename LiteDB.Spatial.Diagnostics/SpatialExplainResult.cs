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
    private static readonly ReadOnlyCollection<SpatialIndexRange> EmptyRanges = new(Array.Empty<SpatialIndexRange>());

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialExplainResult"/> class.
    /// </summary>
    /// <param name="engineName">The name of the engine responsible for the plan.</param>
    /// <param name="dimensions">The dimensionality of the plan.</param>
    /// <param name="coveringBounds">Optional coarse covering bounds.</param>
    /// <param name="indexRanges">The index ranges that will be scanned.</param>
    /// <param name="exactPredicate">Description of the exact predicate.</param>
    /// <param name="indexFieldName">The name of the index field involved.</param>
    /// <param name="boundingBoxFieldName">The name of the bounding box field involved.</param>
    public SpatialExplainResult(
        string engineName,
        int dimensions,
        BoundingBox? coveringBounds,
        IEnumerable<SpatialIndexRange> indexRanges,
        string? exactPredicate,
        string indexFieldName,
        string boundingBoxFieldName)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name must be provided.", nameof(engineName));
        }

        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial explain results must describe either two or three dimensions.");
        }

        if (indexRanges == null)
        {
            throw new ArgumentNullException(nameof(indexRanges));
        }

        if (string.IsNullOrWhiteSpace(indexFieldName))
        {
            throw new ArgumentException("Index field name must be provided.", nameof(indexFieldName));
        }

        if (string.IsNullOrWhiteSpace(boundingBoxFieldName))
        {
            throw new ArgumentException("Bounding box field name must be provided.", nameof(boundingBoxFieldName));
        }

        EngineName = engineName;
        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        IndexFieldName = indexFieldName;
        BoundingBoxFieldName = boundingBoxFieldName;
        ExactPredicate = exactPredicate;

        var ranges = indexRanges as IReadOnlyCollection<SpatialIndexRange> ?? indexRanges.ToArray();
        IndexRanges = ranges.Count == 0 ? EmptyRanges : new ReadOnlyCollection<SpatialIndexRange>(ranges.ToArray());
    }

    /// <summary>
    /// Gets the engine name responsible for the plan.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the dimensionality of the plan.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the coarse covering bounds applied before exact filtering.
    /// </summary>
    public BoundingBox? CoveringBounds { get; }

    /// <summary>
    /// Gets the collection of index ranges that will be scanned.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> IndexRanges { get; }

    /// <summary>
    /// Gets the human-readable description of the exact predicate.
    /// </summary>
    public string? ExactPredicate { get; }

    /// <summary>
    /// Gets the name of the index field participating in the query.
    /// </summary>
    public string IndexFieldName { get; }

    /// <summary>
    /// Gets the name of the bounding box field participating in the query.
    /// </summary>
    public string BoundingBoxFieldName { get; }

    /// <summary>
    /// Gets a value indicating whether a covering bounding box is present.
    /// </summary>
    public bool HasCoveringBounds => CoveringBounds.HasValue;

    /// <summary>
    /// Gets the number of index ranges included in the plan.
    /// </summary>
    public int IndexRangeCount => IndexRanges.Count;

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("Engine: ");
        builder.Append(EngineName);
        builder.Append(" (");
        builder.Append(Dimensions.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("D)");

        builder.Append("Index field: ");
        builder.AppendLine(IndexFieldName);

        builder.Append("Bounding box field: ");
        builder.AppendLine(BoundingBoxFieldName);

        builder.Append("Covering bounds: ");
        builder.AppendLine(HasCoveringBounds ? CoveringBounds!.Value.ToString() : "(none)");

        builder.Append("Index ranges (");
        builder.Append(IndexRangeCount.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("):");

        if (IndexRangeCount == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var range in IndexRanges)
            {
                builder.Append("  [");
                builder.Append(range.Start.ToString(CultureInfo.InvariantCulture));
                builder.Append(", ");
                builder.Append(range.End.ToString(CultureInfo.InvariantCulture));
                builder.Append("] (0x");
                builder.Append(range.Start.ToString("X", CultureInfo.InvariantCulture));
                builder.Append(" - 0x");
                builder.Append(range.End.ToString("X", CultureInfo.InvariantCulture));
                builder.AppendLine(")");
            }
        }

        builder.Append("Exact predicate: ");
        builder.AppendLine(string.IsNullOrWhiteSpace(ExactPredicate) ? "(none)" : ExactPredicate);

        return builder.ToString().TrimEnd();
    }
}
