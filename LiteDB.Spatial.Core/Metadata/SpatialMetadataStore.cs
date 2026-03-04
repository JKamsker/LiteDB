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

        if (!string.Equals(collectionName, descriptor.CollectionName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Collection name argument must match descriptor.CollectionName.", nameof(collectionName));
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

        if (document.TryGetValue(descriptor.Options.BoundingBoxFieldName, out var boundingValue) && !boundingValue.IsNull)
        {
            if (!boundingValue.IsArray)
            {
                throw new SpatialMetadataException($"Spatial metadata for collection '{descriptor.CollectionName}' expects '{descriptor.Options.BoundingBoxFieldName}' to be stored as an array.");
            }

            var array = boundingValue.AsArray;
            descriptor.EnsureCompatible(array.Count);

            for (var i = 0; i < array.Count; i++)
            {
                if (!array[i].IsNumber)
                {
                    throw new SpatialMetadataException($"Spatial metadata for collection '{descriptor.CollectionName}' expects '{descriptor.Options.BoundingBoxFieldName}' to contain only numeric values.");
                }
            }
        }

        if (document.TryGetValue(descriptor.Options.IndexFieldName, out var indexValue) && !indexValue.IsNull)
        {
            if (!indexValue.IsInt32 && !indexValue.IsInt64 && !indexValue.IsDecimal && !indexValue.IsDouble)
            {
                throw new SpatialMetadataException($"Spatial metadata for collection '{descriptor.CollectionName}' expects '{descriptor.Options.IndexFieldName}' to be a numeric type.");
            }
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
            ["engine"] = descriptor.EngineName,
            ["dimensions"] = descriptor.Dimensions,
            ["geometryField"] = descriptor.GeometryFieldName,
            ["options"] = optionsDoc,
            ["updatedUtc"] = DateTime.UtcNow
        };

        if (!descriptor.Settings.IsEmpty)
        {
            var engineDoc = new BaseLiteDB.BsonDocument();

            if (descriptor.Settings.Domain is { } domain)
            {
                var values = domain.GetValues();
                var domainArray = new BaseLiteDB.BsonArray();
                for (var i = 0; i < values.Length; i++)
                {
                    domainArray.Add(new BaseLiteDB.BsonValue(values[i]));
                }

                engineDoc["domain"] = domainArray;
            }

            if (descriptor.Settings.DistanceMode is { } mode)
            {
                engineDoc["distanceMode"] = mode.ToString();
            }

            if (engineDoc.Count > 0)
            {
                document["engineSettings"] = engineDoc;
            }
        }

        return document;
    }

    private static SpatialCollectionDescriptor Deserialize(BaseLiteDB.BsonDocument document)
    {
        var engine = document["engine"].AsString;
        var dimensions = document["dimensions"].AsInt32;
        var geometryField = document.TryGetValue("geometryField", out var geometryValue) && geometryValue.IsString
            ? geometryValue.AsString
            : SpatialCollectionDescriptor.DefaultGeometryFieldName;
        var optionsDoc = document["options"].AsDocument;

        var options = new SpatialIndexOptions(
            optionsDoc["precisionBits"].AsInt32,
            optionsDoc["maxCoveringCells"].AsInt32,
            optionsDoc["distanceTolerance"].AsDouble,
            optionsDoc["indexFieldName"].AsString,
            optionsDoc["boundingBoxFieldName"].AsString);

        SpatialEngineSettings settings = SpatialEngineSettings.Empty;

        if (document.TryGetValue("engineSettings", out var engineSettingsValue) && engineSettingsValue.IsDocument)
        {
            var engineDoc = engineSettingsValue.AsDocument;
            BoundingBox? domain = null;

            if (engineDoc.TryGetValue("domain", out var domainValue) && domainValue.IsArray)
            {
                var domainArray = domainValue.AsArray;
                Span<double> values = domainArray.Count switch
                {
                    4 => stackalloc double[4],
                    6 => stackalloc double[6],
                    _ => Span<double>.Empty
                };

                if (!values.IsEmpty)
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = domainArray[i].AsDouble;
                    }

                    domain = BoundingBox.Create(values);
                }
            }

            GeographicDistanceMode? distanceMode = null;
            if (engineDoc.TryGetValue("distanceMode", out var modeValue) && modeValue.IsString)
            {
                if (Enum.TryParse<GeographicDistanceMode>(modeValue.AsString, ignoreCase: true, out var parsed))
                {
                    distanceMode = parsed;
                }
            }

            settings = SpatialEngineSettings.Create(domain, distanceMode);
        }

        var collectionName = document["collection"].AsString;
        return new SpatialCollectionDescriptor(collectionName, engine, dimensions, geometryField, options, settings);
    }
}
