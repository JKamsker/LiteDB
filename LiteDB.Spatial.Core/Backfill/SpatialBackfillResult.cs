#nullable enable

using System.Collections.Generic;

namespace LiteDB.Spatial;

/// <summary>
/// Summarizes the outcome of a spatial backfill operation.
/// </summary>
public sealed class SpatialBackfillResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialBackfillResult"/> class.
    /// </summary>
    public SpatialBackfillResult(int processed, int updated, int skipped, IReadOnlyList<SpatialBackfillError> errors)
    {
        Processed = processed;
        Updated = updated;
        Skipped = skipped;
        Errors = errors;
    }

    /// <summary>
    /// Gets the total number of documents inspected during the backfill.
    /// </summary>
    public int Processed { get; }

    /// <summary>
    /// Gets the number of documents that were updated with new spatial fields.
    /// </summary>
    public int Updated { get; }

    /// <summary>
    /// Gets the number of documents that already contained up-to-date spatial metadata.
    /// </summary>
    public int Skipped { get; }

    /// <summary>
    /// Gets the collection of errors raised while processing documents.
    /// </summary>
    public IReadOnlyList<SpatialBackfillError> Errors { get; }
}
