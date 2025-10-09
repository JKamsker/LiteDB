extern alias LiteDbBase;

using System;
using System.Collections.Generic;
using System.Linq;
using BaseLiteDB = LiteDbBase::LiteDB;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Provides utilities to backfill spatial index fields on existing documents.
/// </summary>
public static class SpatialBackfill
{
    /// <summary>
    /// Enriches documents with spatial index fields using the provided engine.
    /// </summary>
    /// <param name="collection">The target collection.</param>
    /// <param name="descriptor">The descriptor describing the expected schema.</param>
    /// <param name="engine">The engine responsible for computing the index values.</param>
    /// <param name="batchSize">The number of documents processed per batch.</param>
    /// <param name="checkpoint">Optional checkpoint representing the last processed identifier.</param>
    /// <returns>A <see cref="SpatialBackfillResult"/> describing the outcome of the operation.</returns>
    public static SpatialBackfillResult Run(
        BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection,
        SpatialCollectionDescriptor descriptor,
        ISpatialEngine engine,
        int batchSize = 256,
        BaseLiteDB.BsonValue? checkpoint = null)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (engine.Dimensions != descriptor.Dimensions)
        {
            throw new SpatialMetadataException($"Engine '{engine.Name}' produces {engine.Dimensions}D output but collection is configured for {descriptor.Dimensions}D.");
        }

        if (engine.Mapper == null)
        {
            throw new InvalidOperationException("The spatial engine does not expose a mapper capable of reading documents.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be positive.");
        }

        var mapper = engine.Mapper;
        var options = descriptor.Options;
        var result = new SpatialBackfillResult();
        var lastCheckpoint = checkpoint;
        var pending = new List<BaseLiteDB.BsonDocument>(batchSize);

        while (true)
        {
            var query = collection.Query()
                .OrderBy(BaseLiteDB.BsonExpression.Create("_id"), BaseLiteDB.Query.Ascending);

            if (lastCheckpoint != null)
            {
                query = query.Where(BaseLiteDB.Query.GT("_id", lastCheckpoint));
            }

            var documents = query
                .Limit(batchSize)
                .ToDocuments()
                .ToList();

            if (documents.Count == 0)
            {
                break;
            }

            foreach (var document in documents)
            {
                if (!document.TryGetValue("_id", out var idValue))
                {
                    result.AddError(BaseLiteDB.BsonValue.Null, "Document is missing an _id field.");
                    continue;
                }

                lastCheckpoint = idValue;
                result.MarkProcessed(idValue);

                try
                {
                    if (!TryExtractPoint(document, mapper, descriptor.Dimensions, out var point2D, out var point3D))
                    {
                        result.AddError(idValue, "Unable to extract a spatial point from the document.");
                        continue;
                    }

                    ulong index;
                    BoundingBox boundingBox;

                    if (descriptor.Dimensions == 2)
                    {
                        index = mapper.Encode(point2D);
                        boundingBox = mapper.GetBoundingBox(point2D);
                    }
                    else
                    {
                        index = mapper.Encode(point3D);
                        boundingBox = mapper.GetBoundingBox(point3D);
                    }

                    descriptor.EnsureCompatible(boundingBox);

                    var needsUpdate = EnsureIndex(document, options.IndexFieldName, index);
                    needsUpdate |= EnsureBoundingBox(document, options.BoundingBoxFieldName, boundingBox);

                    if (needsUpdate)
                    {
                        pending.Add(document);
                        result.MarkUpdated();

                        if (pending.Count >= batchSize)
                        {
                            Flush(collection, pending);
                        }
                    }
                    else
                    {
                        result.MarkSkipped();
                    }
                }
                catch (Exception ex) when (!(ex is SpatialMetadataException))
                {
                    result.AddError(idValue, ex.Message, ex);
                }
            }

            if (pending.Count > 0)
            {
                Flush(collection, pending);
            }
        }

        return result;
    }

    private static bool TryExtractPoint(
        BaseLiteDB.BsonDocument document,
        ISpatialMapper mapper,
        int dimensions,
        out GeoPoint point2D,
        out GeoPoint3D point3D)
    {
        point2D = default;
        point3D = default;

        return dimensions switch
        {
            2 when mapper.TryReadPoint(document, out point2D) => true,
            3 when mapper.TryReadPoint(document, out point3D) => true,
            _ => false
        };
    }

    private static bool EnsureIndex(BaseLiteDB.BsonDocument document, string fieldName, ulong index)
    {
        var existingMatches = document.TryGetValue(fieldName, out var existing)
            && TryConvertIndex(existing, out var existingIndex)
            && existingIndex == index;

        if (existingMatches)
        {
            return false;
        }

        document[fieldName] = CreateIndexValue(index);
        return true;
    }

    private static void Flush(BaseLiteDB.ILiteCollection<BaseLiteDB.BsonDocument> collection, List<BaseLiteDB.BsonDocument> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        collection.Update(pending);
        pending.Clear();
    }

    private static bool EnsureBoundingBox(BaseLiteDB.BsonDocument document, string fieldName, BoundingBox box)
    {
        var values = box.GetValues();
        var expected = new List<BaseLiteDB.BsonValue>(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            expected.Add(new BaseLiteDB.BsonValue(values[i]));
        }

        if (document.TryGetValue(fieldName, out var existing) && existing.IsArray)
        {
            var array = existing.AsArray;
            if (array.Count == expected.Count)
            {
                var matches = true;
                for (var i = 0; i < array.Count; i++)
                {
                    if (!array[i].AsDouble.Equals(expected[i].AsDouble))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return false;
                }
            }
        }

        document[fieldName] = new BaseLiteDB.BsonArray(expected);
        return true;
    }

    private static BaseLiteDB.BsonValue CreateIndexValue(ulong index)
    {
        if (index <= long.MaxValue)
        {
            return new BaseLiteDB.BsonValue((long)index);
        }

        return new BaseLiteDB.BsonValue((decimal)index);
    }

    private static bool TryConvertIndex(BaseLiteDB.BsonValue value, out ulong index)
    {
        switch (value.Type)
        {
            case BaseLiteDB.BsonType.Int32:
                var intValue = value.AsInt32;
                if (intValue < 0)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)intValue;
                return true;
            case BaseLiteDB.BsonType.Int64:
                var longValue = value.AsInt64;
                if (longValue < 0)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)longValue;
                return true;
            case BaseLiteDB.BsonType.Decimal:
                var decimalValue = value.AsDecimal;
                if (decimalValue < 0 || decimalValue > ulong.MaxValue)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)decimalValue;
                return decimalValue == (decimal)index;
            case BaseLiteDB.BsonType.Double:
                var doubleValue = value.AsDouble;
                if (doubleValue < 0 || doubleValue > ulong.MaxValue)
                {
                    index = 0;
                    return false;
                }

                index = (ulong)doubleValue;
                return Math.Abs(doubleValue - index) < 0.5d;
            default:
                index = 0;
                return false;
        }
    }
}
