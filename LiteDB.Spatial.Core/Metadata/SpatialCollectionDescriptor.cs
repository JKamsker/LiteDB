using System;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Describes how a collection is configured for spatial indexing.
/// </summary>
public sealed class SpatialCollectionDescriptor : IEquatable<SpatialCollectionDescriptor>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCollectionDescriptor"/> class.
    /// </summary>
    /// <param name="engine">The engine name responsible for indexing the collection.</param>
    /// <param name="dimensions">The spatial dimensionality supported by the engine.</param>
    /// <param name="options">The index options associated with the collection.</param>
    public SpatialCollectionDescriptor(string engine, int dimensions, SpatialIndexOptions options)
    {
        if (string.IsNullOrWhiteSpace(engine))
        {
            throw new ArgumentException("Engine name must be provided.", nameof(engine));
        }

        if (dimensions != 2 && dimensions != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial collections must be either two- or three-dimensional.");
        }

        Engine = engine;
        Dimensions = dimensions;
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the engine name used to service spatial queries for the collection.
    /// </summary>
    public string Engine { get; }

    /// <summary>
    /// Gets the spatial dimensionality supported by the collection.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the index options associated with the collection.
    /// </summary>
    public SpatialIndexOptions Options { get; }

    /// <summary>
    /// Ensures that the provided bounding box matches the descriptor dimensionality.
    /// </summary>
    /// <param name="box">The bounding box to validate.</param>
    public void EnsureCompatible(BoundingBox box)
    {
        if (box.Dimensions != Dimensions)
        {
            throw new SpatialMetadataException($"Collection is configured for {Dimensions}D geometry but encountered a {box.Dimensions}D bounding box.");
        }
    }

    /// <summary>
    /// Ensures that a stored bounding box representation matches the descriptor dimensionality.
    /// </summary>
    /// <param name="valueCount">The number of serialized bounding box values.</param>
    public void EnsureCompatible(int valueCount)
    {
        var expected = Dimensions == 2 ? 4 : 6;
        if (valueCount != expected)
        {
            throw new SpatialMetadataException($"Collection is configured for {Dimensions}D geometry but {Options.BoundingBoxFieldName} contains {valueCount} values.");
        }
    }

    /// <inheritdoc />
    public bool Equals(SpatialCollectionDescriptor? other)
    {
        if (other is null)
        {
            return false;
        }

        return Dimensions == other.Dimensions
            && string.Equals(Engine, other.Engine, StringComparison.Ordinal)
            && Options.Equals(other.Options);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SpatialCollectionDescriptor descriptor && Equals(descriptor);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(Engine);
            hash = (hash * 397) ^ Dimensions.GetHashCode();
            hash = (hash * 397) ^ Options.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Engine={Engine}, Dimensions={Dimensions}, Options=({Options})";
    }
}
