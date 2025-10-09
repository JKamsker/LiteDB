#nullable enable

extern alias litedb;

using System;
using System.Collections.Generic;
using litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Utilities for enriching existing documents with spatial index data.
/// </summary>
public static class SpatialBackfill
{
    /// <summary>
    /// Populates missing spatial index fields on the specified collection.
    /// </summary>
    /// <param name="collection">The collection to scan.</param>
    /// <param name="descriptor">The descriptor describing the spatial configuration.</param>
    /// <param name="batchSize">The number of documents updated per batch.</param>
    /// <param name="checkpointField">The field used to expose the last processed checkpoint.</param>
    /// <returns>A summary describing the backfill outcome.</returns>
    public static SpatialBackfillResult Run(
        ILiteCollection<BsonDocument> collection,
        SpatialCollectionDescriptor descriptor,
        int batchSize = 128,
        string checkpointField = "_id")
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (descriptor.Engine is null)
        {
            throw new ArgumentException("The descriptor must include a resolved engine instance.", nameof(descriptor));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(checkpointField))
        {
            throw new ArgumentException("Checkpoint field cannot be null or whitespace.", nameof(checkpointField));
        }

        var mapper = descriptor.Engine.Mapper ?? throw new InvalidOperationException("The spatial engine did not expose a mapper.");
        var options = descriptor.Options;

        var updated = 0;
        var skipped = 0;
        var errors = 0;
        BsonValue? lastCheckpoint = null;

        var pending = new List<BsonDocument>(batchSize);

        foreach (var document in collection.FindAll())
        {
            lastCheckpoint = document.TryGetValue(checkpointField, out var checkpoint) ? checkpoint : null;

            try
            {
                if (!document.TryGetValue(descriptor.GeometryFieldName, out var geometryValue) || geometryValue.IsNull)
                {
                    skipped++;
                    continue;
                }

                var changed = descriptor.Dimensions switch
                {
                    2 => Process2D(document, mapper, geometryValue, options),
                    3 => Process3D(document, mapper, geometryValue, options),
                    _ => throw new InvalidOperationException($"Unsupported dimensionality {descriptor.Dimensions}.")
                };

                if (!changed)
                {
                    skipped++;
                    continue;
                }

                pending.Add(document);
                updated++;

                if (pending.Count >= batchSize)
                {
                    collection.Update(pending);
                    pending.Clear();
                }
            }
            catch
            {
                errors++;
            }
        }

        if (pending.Count > 0)
        {
            collection.Update(pending);
        }

        return new SpatialBackfillResult(updated, skipped, errors, lastCheckpoint);
    }

    private static bool Process2D(BsonDocument document, ISpatialMapper mapper, BsonValue geometryValue, SpatialIndexOptions options)
    {
        if (!mapper.TryExtractPoint(geometryValue, out var point))
        {
            throw new InvalidOperationException("Unable to extract a two-dimensional point from the document value.");
        }

        var index = mapper.Encode(point);
        var boundingBox = mapper.GetBoundingBox(point);
        return ApplySpatialFields(document, index, boundingBox, options);
    }

    private static bool Process3D(BsonDocument document, ISpatialMapper mapper, BsonValue geometryValue, SpatialIndexOptions options)
    {
        if (!mapper.TryExtractPoint3D(geometryValue, out var point))
        {
            throw new InvalidOperationException("Unable to extract a three-dimensional point from the document value.");
        }

        var index = mapper.Encode(point);
        var boundingBox = mapper.GetBoundingBox(point);
        return ApplySpatialFields(document, index, boundingBox, options);
    }

    private static bool ApplySpatialFields(BsonDocument document, ulong index, BoundingBox box, SpatialIndexOptions options)
    {
        var changed = false;
        var indexField = options.IndexFieldName;
        var encodedIndex = unchecked((long)index);

        if (!document.TryGetValue(indexField, out var existingIndex) || existingIndex.IsNull || existingIndex.AsInt64 != encodedIndex)
        {
            document[indexField] = encodedIndex;
            changed = true;
        }

        var values = box.GetValues();
        var array = new BsonArray();
        for (var i = 0; i < values.Length; i++)
        {
            array.Add(values[i]);
        }

        if (!document.TryGetValue(options.BoundingBoxFieldName, out var existingBox) || existingBox.IsNull)
        {
            document[options.BoundingBoxFieldName] = array;
            return true;
        }

        if (existingBox.Type != BsonType.Array)
        {
            document[options.BoundingBoxFieldName] = array;
            return true;
        }

        var current = existingBox.AsArray;
        if (current.Count != array.Count)
        {
            document[options.BoundingBoxFieldName] = array;
            return true;
        }

        for (var i = 0; i < array.Count; i++)
        {
            if (!current[i].AsDouble.Equals(array[i].AsDouble))
            {
                document[options.BoundingBoxFieldName] = array;
                return true;
            }
        }

        return changed;
    }
}
