#nullable enable

using System;
using LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a failure encountered while attempting to enrich a document with spatial index fields.
/// </summary>
public sealed class SpatialBackfillError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialBackfillError"/> class.
    /// </summary>
    public SpatialBackfillError(BsonValue documentId, string message, Exception exception)
    {
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Exception = exception;
    }

    /// <summary>
    /// Gets the identifier of the document that failed to backfill.
    /// </summary>
    public BsonValue DocumentId { get; }

    /// <summary>
    /// Gets a human friendly error message describing the failure.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the exception raised during processing. May be <c>null</c> when the error is synthesized.
    /// </summary>
    public Exception? Exception { get; }
}
