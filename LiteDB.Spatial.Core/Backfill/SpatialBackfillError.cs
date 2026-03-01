extern alias LiteDbBase;

using System;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Describes a failure encountered while enriching a document with spatial metadata.
/// </summary>
public sealed class SpatialBackfillError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialBackfillError"/> class.
    /// </summary>
    public SpatialBackfillError(BaseLiteDB.BsonValue documentId, string message, Exception? exception = null)
    {
        DocumentId = documentId;
        Message = message;
        Exception = exception;
    }

    /// <summary>
    /// Gets the identifier of the document that caused the error.
    /// </summary>
    public BaseLiteDB.BsonValue DocumentId { get; }

    /// <summary>
    /// Gets a human-readable description of the failure.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the exception captured during backfill, when available.
    /// </summary>
    public Exception? Exception { get; }
}
