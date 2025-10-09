#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Provides helper methods for producing spatial diagnostic summaries.
/// </summary>
public static class SpatialDiagnostics
{
    /// <summary>
    /// Generates an explain result describing the provided spatial plan.
    /// </summary>
    /// <param name="descriptor">The descriptor associated with the collection executing the plan.</param>
    /// <param name="plan">The spatial query plan.</param>
    /// <returns>A printable explain result.</returns>
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

        return new SpatialExplainResult(descriptor, plan);
    }
}

