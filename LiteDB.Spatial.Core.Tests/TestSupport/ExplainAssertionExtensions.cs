using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests;

internal static class ExplainAssertionExtensions
{
    public static ExplainLayout AnalyzeLayout(this SpatialExplainResult explain)
    {
        if (explain == null)
        {
            throw new ArgumentNullException(nameof(explain));
        }

        var lines = explain
            .ToString()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        return new ExplainLayout(
            IndexOf(lines, "Index field:"),
            IndexOf(lines, "Bounding box field:"),
            IndexOf(lines, "Covering bounds"),
            IndexOf(lines, "Exact predicate:"));
    }

    public static void ShouldHighlightIndexPrefilters(this SpatialExplainResult explain)
    {
        var layout = explain.AnalyzeLayout();

        layout.IndexFieldLine.Should().BeGreaterOrEqualTo(0, "the explain output must report the index field");
        layout.BoundingFieldLine.Should().BeGreaterOrEqualTo(0, "the explain output must report the bounding box field");
        layout.ExactPredicateLine.Should().BeGreaterThan(layout.IndexFieldLine, "index hints should appear before exact filters");
        layout.ExactPredicateLine.Should().BeGreaterThan(layout.BoundingFieldLine, "bounding box hints should appear before exact filters");

        explain.IndexRanges.Should().NotBeEmpty("a spatial plan should scan at least one range");
        explain.IndexRanges.Should().NotContain(
            range => range.Start == 0 && range.End == ulong.MaxValue,
            "a full keyspace scan indicates the planner fell back to a table scan");

        explain.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName);
        explain.BoundingBoxFieldName.Should().Be(SpatialIndexOptions.DefaultBoundingBoxFieldName);
    }

    private static int IndexOf(IReadOnlyList<string> lines, string prefix)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

internal readonly record struct ExplainLayout(int IndexFieldLine, int BoundingFieldLine, int CoveringBoundsLine, int ExactPredicateLine);
