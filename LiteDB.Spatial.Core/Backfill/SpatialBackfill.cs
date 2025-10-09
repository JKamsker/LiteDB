#nullable enable

extern alias litedb;
using System;
using System.Collections.Generic;
using LiteDbRuntime = litedb::LiteDB;

namespace LiteDB.Spatial;

/// <summary>
/// Provides utilities for backfilling spatial index fields on existing collections.
/// </summary>
public static class SpatialBackfill
{
    /// <summary>
    /// Executes a spatial backfill for the specified collection.
    /// </summary>
    /// <param name="collection">The target collection.</param>
    /// <param name="descriptor">The spatial descriptor describing the collection.</param>
    /// <param name="batchSize">The number of documents to update per batch.</param>
    /// <param name="resumeAfter">Optional checkpoint identifier used to resume large backfills.</param>
    public static SpatialBackfillResult Run(LiteDbRuntime.ILiteCollection<LiteDbRuntime.BsonDocument> collection, SpatialCollectionDescriptor descriptor, int batchSize, LiteDbRuntime.BsonValue? resumeAfter = null)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!descriptor.HasEngine)
        {
            throw new InvalidOperationException("A runtime engine must be attached to the descriptor before executing the backfill.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be a positive number.");
        }

        var result = new SpatialBackfillResult();
        var mapper = descriptor.Engine!.Mapper;
        var options = descriptor.Options;
        var expectedLength = descriptor.ExpectedBoundingBoxLength;
        var indexField = options.IndexFieldName;
        var boundingField = options.BoundingBoxFieldName;
        var buffer = new List<LiteDbRuntime.BsonDocument>(batchSize);
        LiteDbRuntime.BsonValue? lastId = null;

        foreach (var document in collection.FindAll())
        {
            if (!document.TryGetValue("_id", out var identifier))
            {
                result.ErrorCount++;
                continue;
            }

            if (resumeAfter != null && !resumeAfter.IsNull && identifier.CompareTo(resumeAfter) <= 0)
            {
                continue;
            }

            lastId = identifier;

            var hasIndex = document.TryGetValue(indexField, out var indexValue) && indexValue.IsNumber;
            var hasBounding = document.TryGetValue(boundingField, out var boundingValue) && boundingValue.IsArray;

            if (hasBounding)
            {
                descriptor.ValidateBoundingBoxValue(boundingValue, boundingField);
            }

            if (hasIndex && hasBounding && boundingValue!.AsArray.Count == expectedLength)
            {
                result.SkippedCount++;
                continue;
            }

            if (!mapper.TryMapDocument(document, options, out var index, out var boundingBox))
            {
                result.ErrorCount++;
                continue;
            }

            descriptor.ValidateBoundingBox(boundingBox);

            document[indexField] = ToIndexValue(index);
            document[boundingField] = ToBoundingArray(boundingBox);

            buffer.Add(document);
            result.UpdatedCount++;

            if (buffer.Count >= batchSize)
            {
                Flush(collection, buffer);
            }
        }

        if (buffer.Count > 0)
        {
            Flush(collection, buffer);
        }

        result.LastCheckpoint = lastId;
        return result;
    }

    private static void Flush(LiteDbRuntime.ILiteCollection<LiteDbRuntime.BsonDocument> collection, List<LiteDbRuntime.BsonDocument> buffer)
    {
        collection.Update(buffer);
        buffer.Clear();
    }

    private static LiteDbRuntime.BsonValue ToIndexValue(ulong index)
    {
        if (index <= long.MaxValue)
        {
            return new LiteDbRuntime.BsonValue((long)index);
        }

        return new LiteDbRuntime.BsonValue((decimal)index);
    }

    private static LiteDbRuntime.BsonArray ToBoundingArray(BoundingBox boundingBox)
    {
        var values = boundingBox.GetValues();
        var array = new LiteDbRuntime.BsonArray();

        for (var i = 0; i < values.Length; i++)
        {
            array.Add(new LiteDbRuntime.BsonValue(values[i]));
        }

        return array;
    }
}
