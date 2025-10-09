#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Describes the spatial configuration associated with a LiteDB collection.
/// </summary>
public sealed class SpatialCollectionMetadata : IEquatable<SpatialCollectionMetadata>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCollectionMetadata"/> class.
    /// </summary>
    /// <param name="collectionName">The owning collection name.</param>
    /// <param name="engineName">The spatial engine identifier.</param>
    /// <param name="dimensions">The number of spatial dimensions supported by the engine.</param>
    /// <param name="options">The index options associated with the collection.</param>
    public SpatialCollectionMetadata(string collectionName, string engineName, int dimensions, SpatialIndexOptions options)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or empty.", nameof(collectionName));
        }

        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name cannot be null or empty.", nameof(engineName));
        }

        if (dimensions != 2 && dimensions != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial metadata currently supports only two or three dimensions.");
        }

        CollectionName = collectionName;
        EngineName = engineName;
        Dimensions = dimensions;
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the name of the collection that owns this metadata entry.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the identifier of the spatial engine responsible for the collection.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the number of spatial dimensions tracked for the collection.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the index options used for encoding spatial data.
    /// </summary>
    public SpatialIndexOptions Options { get; }

    /// <summary>
    /// Gets the expected number of elements in the stored bounding box arrays.
    /// </summary>
    public int BoundingBoxElementCount => Dimensions * 2;

    /// <inheritdoc />
    public bool Equals(SpatialCollectionMetadata? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(CollectionName, other.CollectionName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(EngineName, other.EngineName, StringComparison.Ordinal)
            && Dimensions == other.Dimensions
            && Options.Equals(other.Options);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SpatialCollectionMetadata metadata && Equals(metadata);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(CollectionName);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EngineName);
            hash = (hash * 397) ^ Dimensions;
            hash = (hash * 397) ^ Options.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Collection={CollectionName}, Engine={EngineName}, Dimensions={Dimensions}, Options=[{Options}]";
    }
}
