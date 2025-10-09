#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the high level details for a spatial query plan produced by an engine.
/// </summary>
public sealed class SpatialExplainResult
{
    private readonly IReadOnlyList<SpatialIndexRange> _ranges;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialExplainResult"/> class.
    /// </summary>
    /// <param name="engineName">The name of the engine that produced the plan.</param>
    /// <param name="dimensions">The dimensionality of the plan.</param>
    /// <param name="coveringBounds">The optional coarse bounding box evaluated prior to exact predicates.</param>
    /// <param name="ranges">The index ranges that will be scanned.</param>
    /// <param name="exactPredicate">The human readable exact predicate description.</param>
    /// <param name="covering">The covering metrics describing Morton range production.</param>
    /// <param name="indexFieldName">Optional name of the index field used during scanning.</param>
    /// <param name="boundingBoxFieldName">Optional name of the bounding box field storing coarse bounds.</param>
    public SpatialExplainResult(
        string engineName,
        int dimensions,
        BoundingBox? coveringBounds,
        IReadOnlyList<SpatialIndexRange> ranges,
        string? exactPredicate,
        SpatialCoveringResult covering,
        string? indexFieldName = null,
        string? boundingBoxFieldName = null)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name must be provided.", nameof(engineName));
        }

        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Explain results must represent either two or three dimensions.");
        }

        EngineName = engineName;
        Dimensions = dimensions;
        CoveringBounds = coveringBounds;
        _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        ExactPredicate = exactPredicate;
        IndexFieldName = string.IsNullOrWhiteSpace(indexFieldName) ? null : indexFieldName;
        BoundingBoxFieldName = string.IsNullOrWhiteSpace(boundingBoxFieldName) ? null : boundingBoxFieldName;
        Covering = covering ?? throw new ArgumentNullException(nameof(covering));
    }

    /// <summary>
    /// Gets the name of the engine that produced the plan.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the dimensionality of the plan.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the optional covering bounds evaluated before exact predicates.
    /// </summary>
    public BoundingBox? CoveringBounds { get; }

    /// <summary>
    /// Gets the index ranges that will be scanned.
    /// </summary>
    public IReadOnlyList<SpatialIndexRange> IndexRanges => _ranges;

    /// <summary>
    /// Gets a human readable description of the exact predicate applied after index pruning.
    /// </summary>
    public string? ExactPredicate { get; }

    /// <summary>
    /// Gets the document field that stores encoded index values when known.
    /// </summary>
    public string? IndexFieldName { get; }

    /// <summary>
    /// Gets the document field that stores bounding box values when known.
    /// </summary>
    public string? BoundingBoxFieldName { get; }

    /// <summary>
    /// Gets the number of index ranges described by the plan.
    /// </summary>
    public int RangeCount => _ranges.Count;

    /// <summary>
    /// Gets covering statistics describing Morton range production.
    /// </summary>
    public SpatialCoveringResult Covering { get; }

    /// <summary>
    /// Creates a formatted, human readable summary of the explain result.
    /// </summary>
    /// <returns>A multi-line string describing the engine, ranges, and predicates.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("Engine: ")
            .Append(EngineName)
            .Append(' ')
            .Append('(')
            .Append(Dimensions)
            .Append("D)")
            .AppendLine();

        builder.Append("Index field: ")
            .Append(string.IsNullOrWhiteSpace(IndexFieldName) ? "n/a" : IndexFieldName)
            .AppendLine();

        builder.Append("Bounding box field: ")
            .Append(string.IsNullOrWhiteSpace(BoundingBoxFieldName) ? "n/a" : BoundingBoxFieldName)
            .AppendLine();

        builder.Append("Index ranges (")
            .Append(_ranges.Count.ToString(CultureInfo.InvariantCulture))
            .Append(')');

        if (!string.IsNullOrWhiteSpace(IndexFieldName))
        {
            builder.Append(" via ").Append(IndexFieldName);
        }

        builder.AppendLine();

        foreach (var range in _ranges)
        {
            builder.Append("  - ")
                .Append(FormatRange(range))
                .AppendLine();
        }

        builder.Append("Covering ranges: ")
            .Append(Covering.FinalRangeCount.ToString(CultureInfo.InvariantCulture))
            .Append(" / ")
            .Append(Covering.RequestedMaxCells.ToString(CultureInfo.InvariantCulture));

        if (Covering.WasCapped)
        {
            builder.Append(" (capped)");
        }

        builder.AppendLine();

        builder.Append("Estimated covering cells: ")
            .Append(Covering.EstimatedCellCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        builder.Append("Original covering ranges: ")
            .Append(Covering.OriginalRangeCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        builder.Append("Covering bounds");
        if (!string.IsNullOrWhiteSpace(BoundingBoxFieldName))
        {
            builder.Append(" via ").Append(BoundingBoxFieldName);
        }

        builder.Append(": ");
        builder.Append(CoveringBounds.HasValue ? CoveringBounds.Value.ToString() : "none");
        builder.AppendLine();

        builder.Append("Exact predicate: ");
        builder.Append(string.IsNullOrWhiteSpace(ExactPredicate) ? "none" : ExactPredicate);

        return builder.ToString();
    }

    private static string FormatRange(SpatialIndexRange range)
    {
        static string Hex(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);

        return $"[{range.Start}, {range.End}] ({Hex(range.Start)} - {Hex(range.End)})";
    }
}
