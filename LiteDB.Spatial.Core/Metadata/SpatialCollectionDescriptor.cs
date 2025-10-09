#nullable enable

extern alias litedb;
using System;
using LiteDbRuntime = litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Represents the spatial configuration associated with a collection.
/// </summary>
public sealed class SpatialCollectionDescriptor : IEquatable<SpatialCollectionDescriptor>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCollectionDescriptor"/> class.
    /// </summary>
    /// <param name="engineName">The engine name stored in metadata.</param>
    /// <param name="dimensions">The number of spatial dimensions.</param>
    /// <param name="options">The spatial index options associated with the collection.</param>
    /// <param name="engine">An optional runtime engine instance.</param>
    public SpatialCollectionDescriptor(string engineName, int dimensions, SpatialIndexOptions options, ISpatialEngine? engine = null)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name cannot be null or whitespace.", nameof(engineName));
        }

        if (dimensions != 2 && dimensions != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial descriptors support two or three dimensions.");
        }

        EngineName = engineName;
        Dimensions = dimensions;
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Engine = engine;
    }

    /// <summary>
    /// Gets the name of the engine responsible for indexing the collection.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the number of spatial dimensions associated with the collection.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the spatial index options recorded for the collection.
    /// </summary>
    public SpatialIndexOptions Options { get; }

    /// <summary>
    /// Gets the runtime engine instance when available.
    /// </summary>
    public ISpatialEngine? Engine { get; }

    /// <summary>
    /// Gets a value indicating whether a runtime engine has been attached to the descriptor.
    /// </summary>
    public bool HasEngine => Engine != null;

    /// <summary>
    /// Gets the expected number of values stored in the bounding box field.
    /// </summary>
    public int ExpectedBoundingBoxLength => Dimensions == 3 ? 6 : 4;

    /// <summary>
    /// Creates a new descriptor that is associated with the provided runtime engine.
    /// </summary>
    /// <param name="engine">The engine instance used at runtime.</param>
    public SpatialCollectionDescriptor WithEngine(ISpatialEngine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (!string.Equals(engine.Name, EngineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, $"Cannot attach engine '{engine.Name}' to metadata configured for '{EngineName}'.");
        }

        if (engine.Dimensions != Dimensions)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, $"Engine '{engine.Name}' reports {engine.Dimensions} dimensions but metadata expects {Dimensions}.");
        }

        if (!engine.Options.Equals(Options))
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, "Engine options do not match the persisted metadata.");
        }

        return new SpatialCollectionDescriptor(EngineName, Dimensions, Options, engine);
    }

    /// <summary>
    /// Validates the shape of a bounding box stored on a document.
    /// </summary>
    /// <param name="value">The BSON value representing the bounding box.</param>
    /// <param name="fieldName">An optional field name used to enrich error messages.</param>
    public void ValidateBoundingBoxValue(LiteDbRuntime.BsonValue value, string? fieldName = null)
    {
        if (value == null || value.IsNull)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, BuildBoundingBoxErrorMessage(fieldName, "A bounding box value was expected but null was found."));
        }

        if (!value.IsArray)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, BuildBoundingBoxErrorMessage(fieldName, "Bounding boxes must be stored as arrays."));
        }

        var array = value.AsArray;
        if (array.Count != ExpectedBoundingBoxLength)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, BuildBoundingBoxErrorMessage(fieldName, $"Expected {ExpectedBoundingBoxLength} values but found {array.Count}."));
        }
    }

    /// <summary>
    /// Validates that the provided bounding box matches the descriptor dimensionality.
    /// </summary>
    /// <param name="boundingBox">The bounding box produced at runtime.</param>
    public void ValidateBoundingBox(BoundingBox boundingBox)
    {
        if (boundingBox.Dimensions != Dimensions)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, $"Engine returned a {boundingBox.Dimensions}D bounding box but metadata expects {Dimensions}D values.");
        }
    }

    /// <inheritdoc />
    public bool Equals(SpatialCollectionDescriptor? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!string.Equals(EngineName, other.EngineName, StringComparison.Ordinal))
        {
            return false;
        }

        if (Dimensions != other.Dimensions)
        {
            return false;
        }

        return Options.Equals(other.Options);
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
            var hash = StringComparer.Ordinal.GetHashCode(EngineName);
            hash = (hash * 397) ^ Dimensions;
            hash = (hash * 397) ^ Options.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Engine={EngineName}, Dimensions={Dimensions}, Options=[{Options}]";
    }

    private string BuildBoundingBoxErrorMessage(string? fieldName, string message)
    {
        var field = string.IsNullOrWhiteSpace(fieldName) ? Options.BoundingBoxFieldName : fieldName!;
        return $"Spatial metadata for engine '{EngineName}' ({Dimensions}D) expects field '{field}' to contain {ExpectedBoundingBoxLength} numeric values. {message} Consider re-running the spatial backfill or configuring the collection via UseGeographic/UseCartesian* helpers.";
    }
}
