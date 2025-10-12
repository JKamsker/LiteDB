using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Spatial;

namespace LiteDB.Spatial.Core.Tests.Support;

public sealed class ExplainBreakdown
{
    private ExplainBreakdown(SpatialExplainResult result, IReadOnlyList<string> lines)
    {
        Result = result;
        Lines = lines;
        IndexFieldLine = FindLine("Index field:");
        BoundingFieldLine = FindLine("Bounding box field:");
        IndexRangesLine = FindLine("Index ranges");
        CoveringBoundsLine = FindLine("Covering bounds");
        ExactPredicateLine = FindLine("Exact predicate:");

        int FindLine(string marker)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public SpatialExplainResult Result { get; }

    public IReadOnlyList<string> Lines { get; }

    public int IndexFieldLine { get; }

    public int BoundingFieldLine { get; }

    public int IndexRangesLine { get; }

    public int CoveringBoundsLine { get; }

    public int ExactPredicateLine { get; }

    public static ExplainBreakdown Create(SpatialExplainResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var summary = result.ToString();
        var lines = summary.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new ExplainBreakdown(result, lines);
    }
}
