using System;
using System.Linq;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using LiteDB.Vector.Engine;
using LiteDB.Vector.Utils;

namespace LiteDB.Vector
{
    internal sealed class VectorIndexStrategy : IIndexStrategy
    {
        private const string PluginNotRegisteredMessage = "Vector index support requires the LiteDB.Vector plugin. Install the plugin package and register it via LiteDatabaseOptions.Plugins.";

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
            var existingMetadataBuffer = typedCollection.GetPluginIndexMetadata(name);
            var existingMetadata = existingMetadataBuffer != null ? VectorIndexMetadata.Wrap(existingMetadataBuffer) : null;

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

            var pluginContext = typedSnapshot.Plugins ?? throw VectorCompatibility.PluginRequired(
                operation: "EnsureCustomIndex",
                collection: typedSnapshot.CollectionName,
                strategyKind: this.Kind,
                indexName: name,
                expression: expression.Source,
                options: options,
                pluginContext: typedSnapshot.Plugins);
            var registry = pluginContext.Expressions;
            var metadataEnvelope = this.ExtractMetadata(options);
            var descriptor = this.RequireMetadataDescriptor(pluginContext, metadataEnvelope);

            var tuple = typedCollection.InsertPluginIndex(
                name,
                expression.Source,
                this.IndexTypeCode,
                unique: false,
                descriptor,
                metadataEnvelope.Metadata,
                registry);
            var metadata = VectorIndexMetadata.Wrap(tuple.Metadata);

            var indexer = new IndexService(typedSnapshot, typedSnapshot.Collation, typedSnapshot.MaxItemsCount);
            var data = new DataService(typedSnapshot, typedSnapshot.MaxItemsCount);
            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);

            foreach (var pkNode in new IndexAll("_id", global::LiteDB.Query.Ascending).Run(typedCollection, indexer))
            {
                using (var reader = new BufferReader(data.Read(pkNode.DataBlock)))
                {
                    var doc = reader.ReadDocument(expression.Fields).GetValue();
                    vectorService.Upsert(tuple.Index, metadata, doc, pkNode.DataBlock);
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

            var metadataBuffer = typedCollection.GetPluginIndexMetadata(name);

            if (metadataBuffer == null)
            {
                return false;
            }

            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);
            vectorService.Drop(VectorIndexMetadata.Wrap(metadataBuffer));

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

            var vectorIndexes = typedCollection
                .GetPluginIndexes()
                .Where(x => string.Equals(x.PluginId, VectorPlugin.PluginId, StringComparison.Ordinal))
                .Select(x => (x.Index, VectorIndexMetadata.Wrap(x.Metadata)))
                .ToArray();

            if (vectorIndexes.Length == 0)
            {
                return;
            }

            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);

            foreach (var (index, metadata) in vectorIndexes)
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

            var vectorIndexes = typedCollection
                .GetPluginIndexes()
                .Where(x => string.Equals(x.PluginId, VectorPlugin.PluginId, StringComparison.Ordinal))
                .Select(x => (x.Index, VectorIndexMetadata.Wrap(x.Metadata)))
                .ToArray();

            if (vectorIndexes.Length == 0)
            {
                return;
            }

            var vectorService = VectorIndexServiceFactory.Create(typedSnapshot, typedSnapshot.Collation);

            foreach (var (_, metadata) in vectorIndexes)
            {
                vectorService.Delete(metadata, typedAddress);
            }
        }

        private static Snapshot ExpectSnapshot(object value)
        {
            if (value is Snapshot snapshot)
            {
                return snapshot;
            }

            throw new LiteException(0, $"{PluginNotRegisteredMessage} Snapshot context was not recognized.");
        }

        private static CollectionPage ExpectCollection(object value)
        {
            if (value is CollectionPage collection)
            {
                return collection;
            }

            throw new LiteException(0, $"{PluginNotRegisteredMessage} Collection context was not recognized.");
        }

        private static PageAddress ExpectPageAddress(object value)
        {
            if (value is PageAddress address)
            {
                return address;
            }

            throw new LiteException(0, $"{PluginNotRegisteredMessage} Page address context was not recognized.");
        }

        private PluginMetadataEnvelope ExtractMetadata(BsonDocument options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!options.TryGetValue("_pluginMetadata", out var envelopeValue) || envelopeValue == null)
            {
                throw new LiteException(0, "Vector index metadata envelope is missing. Ensure LiteDB.Vector extensions are up to date.");
            }

            if (envelopeValue.IsDocument == false)
            {
                throw new LiteException(0, "Vector index metadata envelope must be a BSON document.");
            }

            var envelope = envelopeValue.AsDocument;

            if (!envelope.TryGetValue("pluginId", out var pluginIdValue) || pluginIdValue.IsString == false)
            {
                throw new LiteException(0, "Vector index metadata envelope is missing the pluginId.");
            }

            if (!envelope.TryGetValue("indexKind", out var indexKindValue) || indexKindValue.IsString == false)
            {
                throw new LiteException(0, "Vector index metadata envelope is missing the indexKind.");
            }

            if (!envelope.TryGetValue("payload", out var payloadValue) || payloadValue.IsDocument == false)
            {
                throw new LiteException(0, "Vector index metadata envelope is missing the payload document.");
            }

            var metadata = new BsonDocument();
            payloadValue.AsDocument.CopyTo(metadata);

            return new PluginMetadataEnvelope(pluginIdValue.AsString, indexKindValue.AsString, metadata);
        }

        private PluginIndexMetadataDescriptor RequireMetadataDescriptor(ILitePluginContext pluginContext, PluginMetadataEnvelope envelope)
        {
            if (pluginContext?.IndexMetadata == null)
            {
                throw VectorCompatibility.PluginRequired(
                    operation: "EnsureCustomIndex",
                    collection: null,
                    strategyKind: this.Kind,
                    indexName: null,
                    expression: envelope?.IndexKind,
                    options: envelope?.Metadata,
                    pluginContext: pluginContext);
            }

            if (!pluginContext.IndexMetadata.TryGet(envelope.IndexKind, out var descriptor))
            {
                throw new LiteException(0, $"Vector metadata descriptor '{envelope.IndexKind}' is not registered.");
            }

            if (!string.Equals(descriptor.PluginId, envelope.PluginId, StringComparison.Ordinal))
            {
                throw new LiteException(0, $"Vector metadata descriptor for plugin '{envelope.PluginId}' was not found.");
            }

            return descriptor;
        }

        private sealed class PluginMetadataEnvelope
        {
            public PluginMetadataEnvelope(string pluginId, string indexKind, BsonDocument metadata)
            {
                PluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
                IndexKind = indexKind ?? throw new ArgumentNullException(nameof(indexKind));
                Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            }

            public string PluginId { get; }

            public string IndexKind { get; }

            public BsonDocument Metadata { get; }
        }

        private (ushort Dimensions, VectorDistanceMetric Metric) ParseOptions(BsonDocument options)
        {
            if (!options.TryGetValue("dimensions", out var dimensionValue) || !dimensionValue.IsNumber)
            {
                throw new LiteException(0, "Vector index options must include a numeric 'dimensions' value.");
            }

            var dimensionNumber = dimensionValue.AsInt32;

            if (dimensionNumber <= 0 || dimensionNumber > ushort.MaxValue)
            {
                throw new LiteException(0, $"Vector dimension limit ({ushort.MaxValue}) exceeded or invalid value provided.");
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
                var raw = metricValue.AsInt32;

                if (raw < byte.MinValue || raw > byte.MaxValue)
                {
                    throw new LiteException(0, "Vector index 'metric' option must be numeric or one of 'euclidean', 'cosine', or 'dotproduct'.");
                }

                var candidate = (VectorDistanceMetric)(byte)raw;

                if (!Enum.IsDefined(typeof(VectorDistanceMetric), candidate))
                {
                    throw new LiteException(0, "Vector index 'metric' option must be numeric or one of 'euclidean', 'cosine', or 'dotproduct'.");
                }

                metric = candidate;
            }
            else if (metricValue.IsString && Enum.TryParse<VectorDistanceMetric>(metricValue.AsString, true, out var parsedMetric))
            {
                metric = parsedMetric;
            }
            else
            {
                throw new LiteException(0, "Vector index 'metric' option must be numeric or one of 'euclidean', 'cosine', or 'dotproduct'.");
            }

            var dimensions = (ushort)dimensionNumber;

            return (dimensions, metric);
        }
    }
}
