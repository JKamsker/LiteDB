#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a human-readable summary of a spatial query plan.
/// </summary>
public sealed class SpatialExplainResult
{
    internal SpatialExplainResult(
        SpatialCollectionDescriptor descriptor,
        ISpatialQueryPlan plan)
    {
        CollectionName = descriptor?.CollectionName ?? throw new ArgumentNullException(nameof(descriptor));
        EngineName = descriptor.EngineName;
        Dimensions = plan?.Dimensions ?? throw new ArgumentNullException(nameof(plan));
        IndexFieldName = descriptor.Options.IndexFieldName;
        BoundingBoxFieldName = descriptor.Options.BoundingBoxFieldName;
        CoveringBounds = plan.CoveringBounds;
        ExactPredicate = plan.ExactPredicateDescription;
        IndexRanges = plan.IndexRanges?.ToArray() ?? Array.Empty<SpatialIndexRange>();

        Summary = BuildSummary();
    }

    /// <summary>
    /// Gets the name of the collection associated with the plan.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the name of the engine that produced the plan.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the number of spatial dimensions handled by the plan.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the name of the index field used to persist Morton/Hilbert codes.
    /// </summary>
    public string IndexFieldName { get; }

    /// <summary>
    /// Gets the name of the bounding box field stored alongside indexed documents.
    /// </summary>
    public string BoundingBoxFieldName { get; }

    /// <summary>
    /// Gets the coarse bounding box applied before exact predicate evaluation.
    /// </summary>
    public BoundingBox? CoveringBounds { get; }

    /// <summary>
    /// Gets the collection of index ranges scanned by the plan.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> IndexRanges { get; }

    /// <summary>
    /// Gets the number of index ranges.
    /// </summary>
    public int RangeCount => IndexRanges.Count;

    /// <summary>
    /// Gets the textual description of the exact predicate.
    /// </summary>
    public string? ExactPredicate { get; }

    /// <summary>
    /// Gets the printable summary of the plan.
    /// </summary>
    public string Summary { get; }

    /// <inheritdoc />
    public override string ToString() => Summary;

    private string BuildSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Collection : {CollectionName}");
        builder.AppendLine($"Engine     : {EngineName} ({Dimensions}D)");
        builder.AppendLine($"IndexField : {IndexFieldName}");
        builder.AppendLine($"BBoxField  : {BoundingBoxFieldName}");
        builder.AppendLine($"Covering   : {FormatBoundingBox(CoveringBounds)}");
        builder.AppendLine($"Ranges ({RangeCount}):");

        if (RangeCount == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var range in IndexRanges)
            {
                builder.AppendLine($"  {range}");
            }
        }

        builder.Append("Predicate  : ");
        builder.AppendLine(string.IsNullOrWhiteSpace(ExactPredicate) ? "(none)" : ExactPredicate);

        return builder.ToString();
    }

    private static string FormatBoundingBox(BoundingBox? bounds)
    {
        return bounds.HasValue ? bounds.Value.ToString() : "(none)";
    }
}

