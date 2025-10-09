#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents a recoverable error encountered while backfilling spatial metadata.
/// </summary>
public sealed class SpatialBackfillException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialBackfillException"/> class.
    /// </summary>
    public SpatialBackfillException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialBackfillException"/> class with an inner exception.
    /// </summary>
    public SpatialBackfillException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
