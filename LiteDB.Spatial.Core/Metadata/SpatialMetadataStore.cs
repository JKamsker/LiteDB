#nullable enable

extern alias litedb;
using System;
using LiteDbRuntime = litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Persists and retrieves spatial metadata associated with collections.
/// </summary>
public static class SpatialMetadataStore
{
    /// <summary>
    /// The name of the collection used to persist spatial metadata.
    /// </summary>
    public const string MetadataCollectionName = "_spatial_meta";

    /// <summary>
    /// Persists metadata for the specified collection.
    /// </summary>
    /// <param name="database">The LiteDB database instance.</param>
    /// <param name="collectionName">The target collection name.</param>
    /// <param name="descriptor">The descriptor to persist.</param>
    public static void Persist(LiteDbRuntime.LiteDatabase database, string collectionName, SpatialCollectionDescriptor descriptor)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
        }

        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var metadataCollection = database.GetCollection<LiteDbRuntime.BsonDocument>(MetadataCollectionName);
        metadataCollection.EnsureIndex("collection");

        var document = new LiteDbRuntime.BsonDocument
        {
            ["_id"] = new LiteDbRuntime.BsonValue(collectionName),
            ["collection"] = new LiteDbRuntime.BsonValue(collectionName),
            ["engine"] = new LiteDbRuntime.BsonValue(descriptor.EngineName),
            ["dimensions"] = new LiteDbRuntime.BsonValue(descriptor.Dimensions),
            ["options"] = SerializeOptions(descriptor.Options),
            ["updatedUtc"] = new LiteDbRuntime.BsonValue(DateTime.UtcNow)
        };

        metadataCollection.Upsert(document);
    }

    /// <summary>
    /// Attempts to retrieve metadata for the specified collection.
    /// </summary>
    /// <param name="database">The LiteDB database instance.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <returns>The descriptor when found; otherwise, <c>null</c>.</returns>
    public static SpatialCollectionDescriptor? TryGet(LiteDbRuntime.LiteDatabase database, string collectionName)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
        }

        var metadataCollection = database.GetCollection<LiteDbRuntime.BsonDocument>(MetadataCollectionName);
        var document = metadataCollection.FindById(collectionName);
        if (document == null)
        {
            return null;
        }

        return DeserializeDescriptor(document);
    }

    /// <summary>
    /// Retrieves metadata for the specified collection, throwing when none is available.
    /// </summary>
    /// <param name="database">The LiteDB database instance.</param>
    /// <param name="collectionName">The collection name.</param>
    public static SpatialCollectionDescriptor GetRequired(LiteDbRuntime.LiteDatabase database, string collectionName)
    {
        var descriptor = TryGet(database, collectionName);
        if (descriptor == null)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.COLLECTION_NOT_FOUND, $"Spatial metadata for collection '{collectionName}' was not found. Configure the collection via UseGeographic/UseCartesian helpers before running spatial queries.");
        }

        return descriptor;
    }

    private static LiteDbRuntime.BsonDocument SerializeOptions(SpatialIndexOptions options)
    {
        return new LiteDbRuntime.BsonDocument
        {
            ["precisionBits"] = new LiteDbRuntime.BsonValue(options.PrecisionBits),
            ["maxCoveringCells"] = new LiteDbRuntime.BsonValue(options.MaxCoveringCells),
            ["distanceTolerance"] = new LiteDbRuntime.BsonValue(options.DistanceTolerance),
            ["indexFieldName"] = new LiteDbRuntime.BsonValue(options.IndexFieldName),
            ["boundingBoxFieldName"] = new LiteDbRuntime.BsonValue(options.BoundingBoxFieldName)
        };
    }

    private static SpatialIndexOptions DeserializeOptions(LiteDbRuntime.BsonDocument document)
    {
        var precisionBits = document.TryGetValue("precisionBits", out var precisionValue) && precisionValue.IsNumber
            ? precisionValue.AsInt32
            : SpatialIndexOptions.DefaultPrecision;

        var maxCells = document.TryGetValue("maxCoveringCells", out var cellValue) && cellValue.IsNumber
            ? cellValue.AsInt32
            : SpatialIndexOptions.DefaultMaxCoveringCells;

        var tolerance = document.TryGetValue("distanceTolerance", out var toleranceValue) && toleranceValue.IsNumber
            ? toleranceValue.AsDouble
            : SpatialIndexOptions.DefaultDistanceTolerance;

        var indexField = document.TryGetValue("indexFieldName", out var indexFieldValue) && indexFieldValue.IsString
            ? indexFieldValue.AsString
            : SpatialIndexOptions.DefaultIndexFieldName;

        var boundingField = document.TryGetValue("boundingBoxFieldName", out var boundingFieldValue) && boundingFieldValue.IsString
            ? boundingFieldValue.AsString
            : SpatialIndexOptions.DefaultBoundingBoxFieldName;

        return new SpatialIndexOptions(precisionBits, maxCells, tolerance, indexField, boundingField);
    }

    private static SpatialCollectionDescriptor DeserializeDescriptor(LiteDbRuntime.BsonDocument document)
    {
        if (!document.TryGetValue("engine", out var engineValue) || !engineValue.IsString)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, "Spatial metadata is missing the engine name.");
        }

        if (!document.TryGetValue("dimensions", out var dimensionsValue) || !dimensionsValue.IsNumber)
        {
            throw new LiteDbRuntime.LiteException(LiteDbRuntime.LiteException.INVALID_DATA_TYPE, "Spatial metadata is missing the dimensions value.");
        }

        var options = document.TryGetValue("options", out var optionsValue) && optionsValue.IsDocument
            ? DeserializeOptions(optionsValue.AsDocument)
            : new SpatialIndexOptions();

        return new SpatialCollectionDescriptor(engineValue.AsString, dimensionsValue.AsInt32, options);
    }
}
