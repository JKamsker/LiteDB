#nullable enable

extern alias litedb;

using litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the outcome of a spatial backfill run.
/// </summary>
public readonly struct SpatialBackfillResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialBackfillResult"/> struct.
    /// </summary>
    public SpatialBackfillResult(int updated, int skipped, int errors, BsonValue? lastCheckpoint)
    {
        Updated = updated;
        Skipped = skipped;
        Errors = errors;
        LastCheckpoint = lastCheckpoint;
    }

    /// <summary>
    /// Gets the number of documents that were enriched with spatial data.
    /// </summary>
    public int Updated { get; }

    /// <summary>
    /// Gets the number of documents that already had spatial fields or were skipped.
    /// </summary>
    public int Skipped { get; }

    /// <summary>
    /// Gets the number of documents that failed during enrichment.
    /// </summary>
    public int Errors { get; }

    /// <summary>
    /// Gets the last processed checkpoint value.
    /// </summary>
    public BsonValue? LastCheckpoint { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Updated={Updated}, Skipped={Skipped}, Errors={Errors}, LastCheckpoint={LastCheckpoint}";
    }
}
