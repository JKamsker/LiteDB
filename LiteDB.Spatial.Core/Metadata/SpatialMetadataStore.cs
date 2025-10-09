#nullable enable

using System;
using LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides persistence for spatial metadata descriptors inside the database.
/// </summary>
public sealed class SpatialMetadataStore
{
    private const string DefaultMetadataCollection = "_spatial_meta";
    private const string EngineField = "engine";
    private const string DimensionsField = "dimensions";
    private const string BoundingBoxField = "bboxElements";
    private const string OptionsField = "options";
    private const string UpdatedField = "updatedUtc";

    private readonly ILiteCollection<BsonDocument> _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialMetadataStore"/> class.
    /// </summary>
    /// <param name="database">The database used to persist metadata.</param>
    /// <param name="collectionName">Optional metadata collection name. Defaults to <c>_spatial_meta</c>.</param>
    public SpatialMetadataStore(ILiteDatabase database, string? collectionName = null)
    {
        if (database is null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        var name = string.IsNullOrWhiteSpace(collectionName) ? DefaultMetadataCollection : collectionName;
        _collection = database.GetCollection(name, BsonAutoId.Int32);
        _collection.EnsureIndex("collection", unique: true);
    }

    /// <summary>
    /// Stores or updates the metadata associated with the provided collection.
    /// </summary>
    /// <param name="metadata">The descriptor to persist.</param>
    public void Save(SpatialCollectionMetadata metadata)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var document = Serialize(metadata);
        _collection.Upsert(metadata.CollectionName, document);
    }

    /// <summary>
    /// Attempts to retrieve the metadata for the specified collection.
    /// </summary>
    public bool TryGet(string collectionName, out SpatialCollectionMetadata? metadata)
    {
        var document = _collection.FindById(collectionName);

        if (document is null)
        {
            metadata = null;
            return false;
        }

        metadata = Deserialize(collectionName, document);
        return true;
    }

    /// <summary>
    /// Retrieves the metadata for the specified collection or throws a descriptive exception if it is missing.
    /// </summary>
    public SpatialCollectionMetadata GetOrThrow(string collectionName, string engineHint)
    {
        if (!TryGet(collectionName, out var metadata) || metadata is null)
        {
            throw SpatialMetadataException.Missing(collectionName, engineHint);
        }

        return metadata;
    }

    private static BsonDocument Serialize(SpatialCollectionMetadata metadata)
    {
        var document = new BsonDocument
        {
            ["_id"] = metadata.CollectionName,
            ["collection"] = metadata.CollectionName,
            [EngineField] = metadata.EngineName,
            [DimensionsField] = metadata.Dimensions,
            [BoundingBoxField] = metadata.BoundingBoxElementCount,
            [UpdatedField] = DateTime.UtcNow
        };

        document[OptionsField] = SerializeOptions(metadata.Options);
        return document;
    }

    private static BsonDocument SerializeOptions(SpatialIndexOptions options)
    {
        return new BsonDocument
        {
            ["precisionBits"] = options.PrecisionBits,
            ["maxCoveringCells"] = options.MaxCoveringCells,
            ["distanceTolerance"] = options.DistanceTolerance,
            ["indexField"] = options.IndexFieldName,
            ["boundingBoxField"] = options.BoundingBoxFieldName
        };
    }

    private static SpatialCollectionMetadata Deserialize(string collectionName, BsonDocument document)
    {
        if (!document.TryGetValue(EngineField, out var engineValue) || engineValue.IsNull || !engineValue.IsString)
        {
            throw SpatialMetadataException.Invalid(collectionName, "Missing engine identifier.");
        }

        if (!document.TryGetValue(DimensionsField, out var dimensionsValue) || !dimensionsValue.IsInt32)
        {
            throw SpatialMetadataException.Invalid(collectionName, "Missing or invalid dimension count.");
        }

        var dimensions = dimensionsValue.AsInt32;

        if (dimensions != 2 && dimensions != 3)
        {
            throw SpatialMetadataException.Invalid(collectionName, $"Unsupported dimension count {dimensions}.");
        }

        if (!document.TryGetValue(BoundingBoxField, out var bboxValue) || !bboxValue.IsInt32)
        {
            throw SpatialMetadataException.Invalid(collectionName, "Missing bounding box element count.");
        }

        var expectedBoundingElements = dimensions * 2;
        var actualBoundingElements = bboxValue.AsInt32;

        if (actualBoundingElements != expectedBoundingElements)
        {
            throw SpatialMetadataException.Invalid(collectionName, $"Expected {expectedBoundingElements} bounding box elements but found {actualBoundingElements}.");
        }

        var options = DeserializeOptions(document[OptionsField] as BsonDocument, new SpatialIndexOptions());

        return new SpatialCollectionMetadata(collectionName, engineValue.AsString, dimensions, options);
    }

    private static SpatialIndexOptions DeserializeOptions(BsonDocument? optionsDocument, SpatialIndexOptions fallback)
    {
        if (optionsDocument is null)
        {
            return fallback;
        }

        var precisionBits = optionsDocument.TryGetValue("precisionBits", out var precisionValue) && precisionValue.IsInt32
            ? precisionValue.AsInt32
            : fallback.PrecisionBits;

        var maxCoveringCells = optionsDocument.TryGetValue("maxCoveringCells", out var cellsValue) && cellsValue.IsInt32
            ? cellsValue.AsInt32
            : fallback.MaxCoveringCells;

        var tolerance = optionsDocument.TryGetValue("distanceTolerance", out var toleranceValue) && toleranceValue.IsNumber
            ? toleranceValue.AsDouble
            : fallback.DistanceTolerance;

        var indexField = optionsDocument.TryGetValue("indexField", out var indexFieldValue) && indexFieldValue.IsString
            ? indexFieldValue.AsString
            : fallback.IndexFieldName;

        var boundingField = optionsDocument.TryGetValue("boundingBoxField", out var boundingFieldValue) && boundingFieldValue.IsString
            ? boundingFieldValue.AsString
            : fallback.BoundingBoxFieldName;

        return new SpatialIndexOptions(precisionBits, maxCoveringCells, tolerance, indexField, boundingField);
    }
}
