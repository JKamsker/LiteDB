using System;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Vector.Engine;

namespace LiteDB.Vector
{
    internal sealed class VectorIndexStrategy : IIndexStrategy
    {
        private readonly ILogger _logger;
        private readonly VectorDistanceMetric? _defaultMetric;

        public VectorIndexStrategy(ILogger logger, VectorDistanceMetric? defaultMetric = null)
        {
            _logger = logger;
            _defaultMetric = defaultMetric;
        }

        public string Kind => "vector";

        public byte IndexTypeCode => 1;

        public bool EnsureIndex(object snapshot, object collection, string name, BsonExpression expression, BsonDocument options)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var typedSnapshot = ExpectSnapshot(snapshot);
            var typedCollection = ExpectCollection(collection);

            var (dimensions, metric) = this.ParseOptions(options);

            var existing = typedCollection.GetCollectionIndex(name);
            var existingMetadata = typedCollection.GetVectorIndexMetadata(name);

            if (existing != null && existing.IndexType != this.IndexTypeCode)
            {
                throw LiteException.IndexAlreadyExist(name);
            }

            if (existing != null && existingMetadata != null)
            {
                if (!string.Equals(existing.Expression, expression.Source, StringComparison.OrdinalIgnoreCase))
                {
                    throw LiteException.IndexAlreadyExist(name);
                }

                if (existingMetadata.Dimensions != dimensions || (VectorDistanceMetric)existingMetadata.Metric != metric)
                {
                    throw new LiteException(0, $"Vector index '{name}' already exists with different options.");
                }

                return false;
            }

            _logger?.Write(LogLevel.Information, $"Creating vector index '{typedSnapshot.CollectionName}.{name}'.");

            var tuple = typedCollection.InsertVectorIndex(name, expression.Source, dimensions, (byte)metric);

            var indexer = new IndexService(typedSnapshot, typedSnapshot.Collation, typedSnapshot.MaxItemsCount);
            var data = new DataService(typedSnapshot, typedSnapshot.MaxItemsCount);
            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);

            foreach (var pkNode in new IndexAll("_id", LiteDB.Query.Ascending).Run(typedCollection, indexer))
            {
                using (var reader = new BufferReader(data.Read(pkNode.DataBlock)))
                {
                    var doc = reader.ReadDocument(expression.Fields).GetValue();
                    vectorService.Upsert(tuple.Index, tuple.Metadata, doc, pkNode.DataBlock);
                }
            }

            return true;
        }

        public bool DropIndex(object snapshot, object collection, string name)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var typedSnapshot = ExpectSnapshot(snapshot);
            var typedCollection = ExpectCollection(collection);

            var metadata = typedCollection.GetVectorIndexMetadata(name);

            if (metadata == null)
            {
                return false;
            }

            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);
            vectorService.Drop(metadata);

            typedCollection.DeleteCollectionIndex(name);

            _logger?.Write(LogLevel.Information, $"Dropped vector index '{typedSnapshot.CollectionName}.{name}'.");

            return true;
        }

        public void OnDocumentUpsert(object snapshot, object collection, object dataBlock, BsonDocument document)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (document == null) throw new ArgumentNullException(nameof(document));

            var typedSnapshot = ExpectSnapshot(snapshot);
            var typedCollection = ExpectCollection(collection);
            var typedAddress = ExpectPageAddress(dataBlock);

            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);

            foreach (var (index, metadata) in typedCollection.GetVectorIndexes())
            {
                vectorService.Upsert(index, metadata, document, typedAddress);
            }
        }

        public void OnDocumentDelete(object snapshot, object collection, object dataBlock)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var typedSnapshot = ExpectSnapshot(snapshot);
            var typedCollection = ExpectCollection(collection);
            var typedAddress = ExpectPageAddress(dataBlock);

            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);

            foreach (var (_, metadata) in typedCollection.GetVectorIndexes())
            {
                vectorService.Delete(metadata, typedAddress);
            }
        }

        private static Snapshot ExpectSnapshot(object value)
        {
            return value as Snapshot ?? throw new ArgumentException("Snapshot context was not recognized.", nameof(value));
        }

        private static CollectionPage ExpectCollection(object value)
        {
            return value as CollectionPage ?? throw new ArgumentException("Collection context was not recognized.", nameof(value));
        }

        private static PageAddress ExpectPageAddress(object value)
        {
            if (value is PageAddress address)
            {
                return address;
            }

            throw new ArgumentException("Page address context was not recognized.", nameof(value));
        }

        private (ushort Dimensions, VectorDistanceMetric Metric) ParseOptions(BsonDocument options)
        {
            if (!options.TryGetValue("dimensions", out var dimensionValue) || !dimensionValue.IsNumber)
            {
                throw new LiteException(0, "Vector index options must include a numeric 'dimensions' value.");
            }

            VectorDistanceMetric metric;

            if (!options.TryGetValue("metric", out var metricValue))
            {
                if (_defaultMetric.HasValue)
                {
                    metric = _defaultMetric.Value;
                }
                else
                {
                    throw new LiteException(0, "Vector index options must include a 'metric' value when no default is configured.");
                }
            }
            else if (metricValue.IsNumber)
            {
                metric = (VectorDistanceMetric)metricValue.AsInt32;
            }
            else if (metricValue.IsString && Enum.TryParse<VectorDistanceMetric>(metricValue.AsString, true, out var parsedMetric))
            {
                metric = parsedMetric;
            }
            else
            {
                throw new LiteException(0, "Vector index 'metric' option must be numeric or one of 'euclidean', 'cosine', or 'dotproduct'.");
            }

            var dimensions = (ushort)dimensionValue.AsInt32;

            return (dimensions, metric);
        }
    }
}
