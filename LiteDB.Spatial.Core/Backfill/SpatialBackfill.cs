#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides utilities for enriching existing documents with spatial index metadata.
/// </summary>
public static class SpatialBackfill
{
    /// <summary>
    /// Populates the configured spatial fields for documents in the provided collection.
    /// </summary>
    /// <param name="collection">The collection containing documents to enrich.</param>
    /// <param name="descriptor">The descriptor describing how to extract spatial values.</param>
    /// <param name="batchSize">The maximum number of documents processed per batch.</param>
    /// <returns>A summary of the operation.</returns>
    public static SpatialBackfillResult Run(ILiteCollection<BsonDocument> collection, SpatialCollectionDescriptor descriptor, int batchSize = 256)
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be a positive number.");
        }

        var errors = new List<SpatialBackfillError>();
        var processed = 0;
        var updated = 0;
        var skipped = 0;

        var skip = 0;
        var query = Query.All();

        while (true)
        {
            var batch = collection.Find(query, skip, batchSize).ToList();

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var document in batch)
            {
                processed++;
                ProcessDocument(collection, descriptor, document, errors, ref updated, ref skipped);
            }

            skip += batch.Count;
        }

        return new SpatialBackfillResult(processed, updated, skipped, errors);
    }

    private static void ProcessDocument(
        ILiteCollection<BsonDocument> collection,
        SpatialCollectionDescriptor descriptor,
        BsonDocument document,
        List<SpatialBackfillError> errors,
        ref int updated,
        ref int skipped)
    {
        var options = descriptor.Metadata.Options;
        var documentId = document["_id"];

        try
        {
            if (documentId.IsNull)
            {
                throw new SpatialBackfillException("Document does not contain an _id field.");
            }

            var hasIndex = HasValidIndex(document, options.IndexFieldName);
            var hasBoundingBox = HasValidBoundingBox(document, options.BoundingBoxFieldName, descriptor.BoundingBoxElementCount);

            if (hasIndex && hasBoundingBox)
            {
                skipped++;
                return;
            }

            BoundingBox boundingBox;
            ulong indexValue;

            if (descriptor.Metadata.Dimensions == 2)
            {
                var point = descriptor.PointAccessor?.Invoke(document);

                if (point is null)
                {
                    throw new SpatialBackfillException($"Document missing 2D geometry for engine \"{descriptor.Metadata.EngineName}\".");
                }

                boundingBox = descriptor.Mapper.GetBoundingBox(point.Value);
                indexValue = descriptor.Mapper.Encode(point.Value);
            }
            else
            {
                var point = descriptor.Point3DAccessor?.Invoke(document);

                if (point is null)
                {
                    throw new SpatialBackfillException($"Document missing 3D geometry for engine \"{descriptor.Metadata.EngineName}\".");
                }

                boundingBox = descriptor.Mapper.GetBoundingBox(point.Value);
                indexValue = descriptor.Mapper.Encode(point.Value);
            }

            document[options.IndexFieldName] = new BsonValue((decimal)indexValue);
            document[options.BoundingBoxFieldName] = ToBsonArray(boundingBox);

            if (!collection.Update(documentId, document))
            {
                throw new SpatialBackfillException("Failed to persist spatial metadata for the document.");
            }

            updated++;
        }
        catch (Exception ex)
        {
            errors.Add(new SpatialBackfillError(documentId, ex.Message, ex));
        }
    }

    private static bool HasValidIndex(BsonDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value))
        {
            return false;
        }

        return value.IsNumber;
    }

    private static bool HasValidBoundingBox(BsonDocument document, string fieldName, int expectedElements)
    {
        if (!document.TryGetValue(fieldName, out var value) || !value.IsArray)
        {
            return false;
        }

        var array = value.AsArray;

        if (array.Count != expectedElements)
        {
            return false;
        }

        for (var i = 0; i < array.Count; i++)
        {
            if (!array[i].IsNumber)
            {
                return false;
            }
        }

        return true;
    }

    private static BsonArray ToBsonArray(BoundingBox boundingBox)
    {
        var result = new BsonArray();
        var values = boundingBox.ToArray();

        for (var i = 0; i < values.Length; i++)
        {
            result.Add(new BsonValue(values[i]));
        }

        return result;
    }
}
