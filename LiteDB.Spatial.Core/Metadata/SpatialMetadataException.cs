#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents errors encountered while reading or writing spatial metadata.
/// </summary>
public sealed class SpatialMetadataException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataException"/> class.
    /// </summary>
    public SpatialMetadataException(string collectionName, string message)
        : base(message)
    {
        CollectionName = collectionName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataException"/> class with an inner exception.
    /// </summary>
    public SpatialMetadataException(string collectionName, string message, Exception innerException)
        : base(message, innerException)
    {
        CollectionName = collectionName;
    }

    /// <summary>
    /// Gets the collection name associated with the metadata failure.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Creates an exception describing a missing metadata descriptor.
    /// </summary>
    public static SpatialMetadataException Missing(string collectionName, string engineHint)
    {
        var message = $"Spatial metadata for collection \"{collectionName}\" was not found. Configure the collection via {engineHint} before issuing spatial queries.";
        return new SpatialMetadataException(collectionName, message);
    }

    /// <summary>
    /// Creates an exception describing an invalid or inconsistent metadata document.
    /// </summary>
    public static SpatialMetadataException Invalid(string collectionName, string reason)
    {
        var message = $"Spatial metadata for collection \"{collectionName}\" is invalid: {reason}";
        return new SpatialMetadataException(collectionName, message);
    }
}
