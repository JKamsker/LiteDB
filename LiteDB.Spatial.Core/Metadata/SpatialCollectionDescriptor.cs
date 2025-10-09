#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the spatial capabilities configured for a collection.
/// </summary>
public sealed class SpatialCollectionDescriptor : IEquatable<SpatialCollectionDescriptor>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCollectionDescriptor"/> class.
    /// </summary>
    /// <param name="collectionName">Name of the collection the descriptor applies to.</param>
    /// <param name="engineName">The logical engine name associated with the collection.</param>
    /// <param name="dimensions">Number of spatial dimensions encoded by the engine.</param>
    /// <param name="geometryFieldName">The document field that stores the spatial value.</param>
    /// <param name="options">Options controlling index persistence and query planning.</param>
    /// <param name="engine">Optional resolved engine instance for runtime operations.</param>
    public SpatialCollectionDescriptor(
        string collectionName,
        string engineName,
        int dimensions,
        string geometryFieldName,
        SpatialIndexOptions options,
        ISpatialEngine? engine = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
        }

        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name cannot be null or whitespace.", nameof(engineName));
        }

        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial collections must be two or three dimensional.");
        }

        if (string.IsNullOrWhiteSpace(geometryFieldName))
        {
            throw new ArgumentException("Geometry field name cannot be null or whitespace.", nameof(geometryFieldName));
        }

        CollectionName = collectionName;
        EngineName = engineName;
        Dimensions = dimensions;
        GeometryFieldName = geometryFieldName;
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Engine = engine;
    }

    /// <summary>
    /// Gets the collection name associated with the descriptor.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the logical engine identifier.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the number of dimensions supported by the collection.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the field containing the spatial value.
    /// </summary>
    public string GeometryFieldName { get; }

    /// <summary>
    /// Gets the options governing index persistence.
    /// </summary>
    public SpatialIndexOptions Options { get; }

    /// <summary>
    /// Gets the runtime engine instance when resolved.
    /// </summary>
    public ISpatialEngine? Engine { get; }

    /// <summary>
    /// Gets the expected number of entries within the stored bounding box.
    /// </summary>
    public int ExpectedBoundingBoxLength => Dimensions * 2;

    /// <summary>
    /// Returns a new descriptor with the provided engine instance attached.
    /// </summary>
    /// <param name="engine">The resolved engine.</param>
    /// <returns>A descriptor that references the supplied engine.</returns>
    public SpatialCollectionDescriptor WithEngine(ISpatialEngine engine)
    {
        if (engine is null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (engine.Dimensions != Dimensions)
        {
            throw new ArgumentException("Engine dimensionality does not match the descriptor.", nameof(engine));
        }

        if (!string.Equals(engine.Name, EngineName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Engine name does not match the descriptor metadata.", nameof(engine));
        }

        return new SpatialCollectionDescriptor(CollectionName, EngineName, Dimensions, GeometryFieldName, Options, engine);
    }

    /// <inheritdoc />
    public bool Equals(SpatialCollectionDescriptor? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(CollectionName, other.CollectionName, StringComparison.Ordinal)
            && string.Equals(EngineName, other.EngineName, StringComparison.Ordinal)
            && Dimensions == other.Dimensions
            && string.Equals(GeometryFieldName, other.GeometryFieldName, StringComparison.Ordinal)
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
            var hash = StringComparer.Ordinal.GetHashCode(CollectionName);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EngineName);
            hash = (hash * 397) ^ Dimensions;
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(GeometryFieldName);
            hash = (hash * 397) ^ Options.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Collection=\"{CollectionName}\", Engine=\"{EngineName}\", Dimensions={Dimensions}, Field=\"{GeometryFieldName}\", Options={Options}";
    }
}
