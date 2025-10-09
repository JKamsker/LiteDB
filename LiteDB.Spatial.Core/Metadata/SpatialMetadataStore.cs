#nullable enable

extern alias litedb;

using System;
using litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Persists spatial configuration descriptors inside a dedicated metadata collection.
/// </summary>
public sealed class SpatialMetadataStore
{
    /// <summary>
    /// Gets the name of the collection that stores spatial metadata.
    /// </summary>
    public const string MetadataCollectionName = "_spatial_meta";

    private readonly ILiteCollection<BsonDocument> _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataStore"/> class.
    /// </summary>
    /// <param name="database">The database used to persist metadata.</param>
    public SpatialMetadataStore(ILiteDatabase database)
    {
        if (database is null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        _collection = database.GetCollection<BsonDocument>(MetadataCollectionName);
    }

    /// <summary>
    /// Persists the provided descriptor and returns the stored value.
    /// </summary>
    public SpatialCollectionDescriptor UpsertDescriptor(SpatialCollectionDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var document = Serialize(descriptor);
        _collection.Upsert(document);
        return descriptor;
    }

    /// <summary>
    /// Attempts to locate spatial metadata for the specified collection.
    /// </summary>
    public SpatialCollectionDescriptor? TryGetDescriptor(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
        }

        var document = _collection.FindById(collectionName);
        return document is null ? null : Deserialize(document);
    }

    /// <summary>
    /// Retrieves the descriptor for the provided collection or throws a descriptive error.
    /// </summary>
    public SpatialCollectionDescriptor GetRequiredDescriptor(string collectionName)
    {
        var descriptor = TryGetDescriptor(collectionName);
        if (descriptor is null)
        {
            throw new InvalidOperationException($"Spatial metadata for collection \"{collectionName}\" was not found. Configure an engine using Spatial.UseGeographic(...) or the appropriate helper for your engine.");
        }

        return descriptor;
    }

    /// <summary>
    /// Ensures that the stored metadata remains compatible with the provided document.
    /// </summary>
    public void ValidateDocument(SpatialCollectionDescriptor descriptor, BsonDocument document)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.TryGetValue(descriptor.Options.BoundingBoxFieldName, out var boundingValue) || boundingValue.IsNull)
        {
            return;
        }

        if (boundingValue.Type != BsonType.Array)
        {
            throw new InvalidOperationException($"The bounding box field \"{descriptor.Options.BoundingBoxFieldName}\" must be stored as an array.");
        }

        var array = boundingValue.AsArray;
        if (array.Count != descriptor.ExpectedBoundingBoxLength)
        {
            throw new InvalidOperationException($"Spatial metadata for collection \"{descriptor.CollectionName}\" declares {descriptor.Dimensions} dimensions but found {array.Count} values within \"{descriptor.Options.BoundingBoxFieldName}\".");
        }

        if (document.TryGetValue(descriptor.Options.IndexFieldName, out var indexValue) && !indexValue.IsNull)
        {
            if (!indexValue.IsInt64 && !indexValue.IsInt32)
            {
                throw new InvalidOperationException($"The index field \"{descriptor.Options.IndexFieldName}\" must be a 64-bit integer value.");
            }
        }
    }

    private static BsonDocument Serialize(SpatialCollectionDescriptor descriptor)
    {
        return new BsonDocument
        {
            ["_id"] = descriptor.CollectionName,
            ["collection"] = descriptor.CollectionName,
            ["engine"] = descriptor.EngineName,
            ["dimensions"] = descriptor.Dimensions,
            ["geometryField"] = descriptor.GeometryFieldName,
            ["options"] = SerializeOptions(descriptor.Options)
        };
    }

    private static BsonDocument SerializeOptions(SpatialIndexOptions options)
    {
        return new BsonDocument
        {
            ["precisionBits"] = options.PrecisionBits,
            ["maxCells"] = options.MaxCoveringCells,
            ["tolerance"] = options.DistanceTolerance,
            ["indexField"] = options.IndexFieldName,
            ["boundingField"] = options.BoundingBoxFieldName
        };
    }

    private static SpatialCollectionDescriptor Deserialize(BsonDocument document)
    {
        var collectionName = document["collection"].AsString;
        var engineName = document["engine"].AsString;
        var dimensions = document["dimensions"].AsInt32;
        var geometryField = document["geometryField"].AsString;
        var optionsDocument = document["options"].AsDocument;

        var options = new SpatialIndexOptions(
            precisionBits: optionsDocument["precisionBits"].AsInt32,
            maxCoveringCells: optionsDocument["maxCells"].AsInt32,
            distanceTolerance: optionsDocument["tolerance"].AsDouble,
            indexFieldName: optionsDocument["indexField"].AsString,
            boundingBoxFieldName: optionsDocument["boundingField"].AsString);

        return new SpatialCollectionDescriptor(collectionName, engineName, dimensions, geometryField, options);
    }
}
