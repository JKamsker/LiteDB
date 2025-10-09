#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Provides diagnostic helpers for spatial query plans.
/// </summary>
public static class SpatialDiagnostics
{
    /// <summary>
    /// Produces a human-readable summary for the provided spatial query plan.
    /// </summary>
    /// <param name="descriptor">The collection descriptor associated with the plan.</param>
    /// <param name="plan">The plan to summarize.</param>
    /// <returns>A <see cref="SpatialExplainResult"/> containing the summary.</returns>
    public static SpatialExplainResult Explain(SpatialCollectionDescriptor descriptor, ISpatialQueryPlan plan)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var engineName = descriptor.Engine?.Name ?? descriptor.EngineName;
        var ranges = plan.IndexRanges ?? Array.Empty<SpatialIndexRange>();

        return new SpatialExplainResult(
            engineName,
            plan.Dimensions,
            plan.CoveringBounds,
            ranges,
            plan.ExactPredicateDescription,
            descriptor.Options.IndexFieldName,
            descriptor.Options.BoundingBoxFieldName);
    }
}
