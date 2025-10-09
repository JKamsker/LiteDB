extern alias LiteDbBase;

using System.Collections.Generic;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Represents the outcome of a spatial backfill operation.
/// </summary>
public sealed class SpatialBackfillResult
{
    private readonly List<SpatialBackfillError> _errors = new();

    internal SpatialBackfillResult()
    {
    }

    /// <summary>
    /// Gets the number of documents processed during the operation.
    /// </summary>
    public int Processed { get; private set; }

    /// <summary>
    /// Gets the number of documents updated with spatial data.
    /// </summary>
    public int Updated { get; private set; }

    /// <summary>
    /// Gets the number of documents that already contained the expected spatial data.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Gets the collection of errors observed during the backfill.
    /// </summary>
    public IReadOnlyList<SpatialBackfillError> Errors => _errors;

    /// <summary>
    /// Gets the identifier of the last processed document, which can be used as a checkpoint.
    /// </summary>
    public BaseLiteDB.BsonValue? LastCheckpoint { get; private set; }

    internal void MarkProcessed(BaseLiteDB.BsonValue checkpoint)
    {
        Processed++;
        LastCheckpoint = checkpoint;
    }

    internal void MarkUpdated()
    {
        Updated++;
    }

    internal void MarkSkipped()
    {
        Skipped++;
    }

    internal void AddError(BaseLiteDB.BsonValue id, string message)
    {
        _errors.Add(new SpatialBackfillError(id, message));
    }
}
