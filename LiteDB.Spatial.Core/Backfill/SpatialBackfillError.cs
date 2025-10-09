extern alias LiteDbBase;

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
    public SpatialBackfillError(BaseLiteDB.BsonValue documentId, string message)
    {
        DocumentId = documentId;
        Message = message;
    }

    /// <summary>
    /// Gets the identifier of the document that caused the error.
    /// </summary>
    public BaseLiteDB.BsonValue DocumentId { get; }

    /// <summary>
    /// Gets a human-readable description of the failure.
    /// </summary>
    public string Message { get; }
}
