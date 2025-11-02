using System;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Represents configuration issues with spatial metadata.
/// </summary>
public sealed class SpatialMetadataException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataException"/> class.
    /// </summary>
    public SpatialMetadataException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataException"/> class using the specified message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public SpatialMetadataException(string message)
        : base(message)
    {
    }
}
