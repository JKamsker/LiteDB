#nullable enable

using System;
using System.Linq;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helper methods for inspecting spatial query plans.
/// </summary>
public static class SpatialDiagnostics
{
    /// <summary>
    /// Produces a human-readable summary of the supplied query plan.
    /// </summary>
    /// <param name="descriptor">The spatial descriptor describing the collection.</param>
    /// <param name="plan">The query plan to summarize.</param>
    /// <returns>A <see cref="SpatialExplainResult"/> containing the formatted summary.</returns>
    public static SpatialExplainResult Explain(SpatialCollectionDescriptor descriptor, ISpatialQueryPlan plan)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var ranges = plan.IndexRanges?.ToArray() ?? Array.Empty<SpatialIndexRange>();
        return new SpatialExplainResult(descriptor.CollectionName, descriptor.EngineName, plan.Dimensions, plan.CoveringBounds, ranges, plan.ExactPredicateDescription);
    }
}
