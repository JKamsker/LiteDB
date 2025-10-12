#nullable enable

using System;
using System.Linq;
using FluentAssertions;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

internal static class SpatialExplainAssertions
{
    public static void AssertIndexedExecution(SpatialExplainResult explain)
    {
        explain.Should().NotBeNull();
        explain.RangeCount.Should().BeGreaterThan(0, "spatial queries should avoid full collection scans");
        explain.IndexFieldName.Should().Be(SpatialIndexOptions.DefaultIndexFieldName, "spatial indexes rely on the '_idx' field");
        explain.BoundingBoxFieldName.Should().Be(SpatialIndexOptions.DefaultBoundingBoxFieldName, "spatial prefilters rely on the '_mbb' field");

        var summary = explain.ToString();
        summary.Should().Contain("Index ranges", "explain output should list Morton ranges");
        summary.Should().Contain("Covering bounds", "explain output should include bounding box prefilters");
        summary.Should().Contain("Exact predicate", "explain output should describe exact filtering");

        var indexLine = FindLine(summary, "Index ranges");
        var boundingLine = FindLine(summary, "Covering bounds");
        var exactLine = FindLine(summary, "Exact predicate");

        indexLine.Should().BeLessThan(exactLine, "range evaluation must occur before exact predicates");
        boundingLine.Should().BeLessThan(exactLine, "bounding-box prefilters must run before exact predicates");
    }

    private static int FindLine(string summary, string marker)
    {
        var lines = summary.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Explain summary did not contain the marker '{marker}'.\n{summary}");
    }
}
