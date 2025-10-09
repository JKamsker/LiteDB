#nullable enable

extern alias litedb;
using LiteDbRuntime = litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the outcome of a spatial backfill operation.
/// </summary>
public sealed class SpatialBackfillResult
{
    internal SpatialBackfillResult()
    {
    }

    /// <summary>
    /// Gets the number of documents that were updated during the backfill.
    /// </summary>
    public int UpdatedCount { get; internal set; }

    /// <summary>
    /// Gets the number of documents that already contained the expected spatial fields.
    /// </summary>
    public int SkippedCount { get; internal set; }

    /// <summary>
    /// Gets the number of documents that could not be processed.
    /// </summary>
    public int ErrorCount { get; internal set; }

    /// <summary>
    /// Gets the identifier of the last processed document, which can be used as a checkpoint.
    /// </summary>
    public LiteDbRuntime.BsonValue? LastCheckpoint { get; internal set; }
}
