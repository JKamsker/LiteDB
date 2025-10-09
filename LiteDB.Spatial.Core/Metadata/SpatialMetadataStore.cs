extern alias LiteDbBase;

using System;
using System.Collections.Concurrent;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Persists spatial metadata describing how a collection is indexed.
/// </summary>
public sealed class SpatialMetadataStore
{
    /// <summary>
    /// The name of the metadata collection.
    /// </summary>
    public const string MetadataCollectionName = "_spatial_meta";

    private readonly BaseLiteDB.ILiteDatabase _database;
    private readonly ConcurrentDictionary<string, SpatialCollectionDescriptor> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataStore"/> class.
    /// </summary>
    /// <param name="database">The database that hosts the metadata collection.</param>
    public SpatialMetadataStore(BaseLiteDB.ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Persists the provided descriptor for the specified collection.
    /// </summary>
    public void SaveDescriptor(string collectionName, SpatialCollectionDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var collection = _database.GetCollection(MetadataCollectionName);
        var document = Serialize(collectionName, descriptor);
        collection.Upsert(document);
        _cache[collectionName] = descriptor;
    }

    /// <summary>
    /// Attempts to retrieve the descriptor for the specified collection.
    /// </summary>
    public bool TryGetDescriptor(string collectionName, out SpatialCollectionDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        if (_cache.TryGetValue(collectionName, out descriptor!))
        {
            return true;
        }

        var collection = _database.GetCollection(MetadataCollectionName);
        var document = collection.FindById(new BaseLiteDB.BsonValue(collectionName));
        if (document == null)
        {
            descriptor = null!;
            return false;
        }

        descriptor = Deserialize(document);
        _cache[collectionName] = descriptor;
        return true;
    }

    /// <summary>
    /// Retrieves the descriptor for the collection or throws a friendly exception if missing.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="suggestion">Optional suggestion guiding the caller to configure the collection.</param>
    /// <returns>The persisted descriptor.</returns>
    public SpatialCollectionDescriptor GetRequiredDescriptor(string collectionName, string? suggestion = null)
    {
        if (TryGetDescriptor(collectionName, out var descriptor))
        {
            return descriptor;
        }

        var hint = suggestion ?? "Configure the collection using the appropriate Spatial.Use* helper.";
        throw new SpatialMetadataException($"Collection '{collectionName}' is not configured for spatial indexing. {hint}");
    }

    /// <summary>
    /// Validates that the provided document aligns with the persisted descriptor.
    /// </summary>
    public void ValidateDocument(BaseLiteDB.BsonDocument document, SpatialCollectionDescriptor descriptor)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (document.TryGetValue(descriptor.Options.BoundingBoxFieldName, out var value) && value.IsArray)
        {
            descriptor.EnsureCompatible(value.AsArray.Count);
        }
    }

    private static BaseLiteDB.BsonDocument Serialize(string collectionName, SpatialCollectionDescriptor descriptor)
    {
        var options = descriptor.Options;
        var optionsDoc = new BaseLiteDB.BsonDocument
        {
            ["precisionBits"] = options.PrecisionBits,
            ["maxCoveringCells"] = options.MaxCoveringCells,
            ["distanceTolerance"] = options.DistanceTolerance,
            ["indexFieldName"] = options.IndexFieldName,
            ["boundingBoxFieldName"] = options.BoundingBoxFieldName,
        };

        var document = new BaseLiteDB.BsonDocument
        {
            ["_id"] = collectionName,
            ["collection"] = collectionName,
            ["engine"] = descriptor.Engine,
            ["dimensions"] = descriptor.Dimensions,
            ["options"] = optionsDoc,
            ["updatedUtc"] = DateTime.UtcNow
        };

        return document;
    }

    private static SpatialCollectionDescriptor Deserialize(BaseLiteDB.BsonDocument document)
    {
        var engine = document["engine"].AsString;
        var dimensions = document["dimensions"].AsInt32;
        var optionsDoc = document["options"].AsDocument;

        var options = new SpatialIndexOptions(
            optionsDoc["precisionBits"].AsInt32,
            optionsDoc["maxCoveringCells"].AsInt32,
            optionsDoc["distanceTolerance"].AsDouble,
            optionsDoc["indexFieldName"].AsString,
            optionsDoc["boundingBoxFieldName"].AsString);

        return new SpatialCollectionDescriptor(engine, dimensions, options);
    }
}
