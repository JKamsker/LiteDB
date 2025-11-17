using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Create a new index (or do nothing if already exists) to a collection/field
        /// </summary>
        public bool EnsureIndex(string collection, string name, BsonExpression expression, bool unique)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (expression.IsIndexable == false) throw new ArgumentException("Index expressions must contains at least one document field. Used methods must be immutable. Parameters are not supported.", nameof(expression));

            if (name.Length > INDEX_NAME_MAX_LENGTH) throw LiteException.InvalidIndexName(name, collection, "MaxLength = " + INDEX_NAME_MAX_LENGTH);
            if (!name.IsWord()) throw LiteException.InvalidIndexName(name, collection, "Use only [a-Z$_]");
            if (name.StartsWith("$")) throw LiteException.InvalidIndexName(name, collection, "Index name can't start with `$`");
            if (expression.IsScalar == false && unique) throw new LiteException(0, "Multikey index expression do not support unique option");

            if (expression.Source == "$._id") return false; // always exists

            return this.AutoTransaction(transaction =>
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, true);
                var collectionPage = snapshot.CollectionPage;
                var indexer = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);
                var data = new DataService(snapshot, _disk.MAX_ITEMS_COUNT);

                // check if index already exists
                var current = collectionPage.GetCollectionIndex(name);

                // if already exists, just exit
                if (current != null)
                {
                    // but if expression are different, throw error
                    if (current.Expression != expression.Source) throw LiteException.IndexAlreadyExist(name);

                    return false;
                }

                LOG($"create index `{collection}.{name}`", "COMMAND");

                // create index head
                var index = indexer.CreateIndex(name, expression.Source, unique);
                var count = 0u;

                // read all objects (read from PK index)
                foreach (var pkNode in new IndexAll("_id", LiteDB.Query.Ascending).Run(collectionPage, indexer))
                {
                    using (var reader = new BufferReader(data.Read(pkNode.DataBlock), utcDate: false, pluginContext: snapshot.Plugins))
                    {
                        var doc = reader.ReadDocument(expression.Fields).GetValue();

                        // first/last node in this document that will be added
                        IndexNode last = null;
                        IndexNode first = null;

                        // get values from expression in document
                        var keys = expression.GetIndexKeys(doc, _header.Pragmas.Collation);

                        // adding index node for each value
                        foreach (var key in keys)
                        {
                            _state.Validate();

                            // insert new index node
                            var node = indexer.AddNode(index, key, pkNode.DataBlock, last);

                            if (first == null) first = node;

                            last = node;

                            count++;
                        }

                        // fix single linked-list in pkNode
                        if (first != null)
                        {
                            last.SetNextNode(pkNode.NextNode);
                            pkNode.SetNextNode(first.Position);
                        }
                    }

                    transaction.Safepoint();
                }

                return true;
            });
        }

        /// <summary>
        /// Create a new plugin-provided index (or do nothing if already exists) for a collection/field.
        /// </summary>
        public bool EnsureCustomIndex(string collection, string name, string strategyKind, BsonExpression expression, BsonDocument options)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));
            if (strategyKind.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(strategyKind));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (expression.Fields.Count == 0) throw new ArgumentException($"Custom index '{strategyKind}' expressions must reference a document field.", nameof(expression));

            if (name.Length > INDEX_NAME_MAX_LENGTH) throw LiteException.InvalidIndexName(name, collection, "MaxLength = " + INDEX_NAME_MAX_LENGTH);
            if (!name.IsWord()) throw LiteException.InvalidIndexName(name, collection, "Use only [a-Z$_]");
            if (name.StartsWith("$")) throw LiteException.InvalidIndexName(name, collection, "Index name can't start with `$`");

            var requestedPluginId = TryGetPluginIdFromOptions(options);
            var strategy = _plugins?.Indexes?.GetByKind(strategyKind) ?? throw this.CreatePluginRequiredException(
                pluginId: requestedPluginId,
                strategyKind: strategyKind,
                operation: "EnsureCustomIndex",
                collection: collection,
                indexName: name,
                expression: expression.Source,
                options: options);

            return this.AutoTransaction(transaction =>
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, true);
                var collectionPage = snapshot.CollectionPage;

                return strategy.EnsureIndex(snapshot, collectionPage, name, expression, options);
            });
        }

        /// <summary>
        /// Drop an index from a collection
        /// </summary>
        public bool DropIndex(string collection, string name)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

            if (name == "_id") throw LiteException.IndexDropId();

            return this.AutoTransaction(transaction =>
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, false);
                var col = snapshot.CollectionPage;
                var indexer = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);
            
                // no collection, no index
                if (col == null) return false;
            
                // search for index reference
                var index = col.GetCollectionIndex(name);
            
                // no index, no drop
                if (index == null) return false;

                if (index.IndexType != 0)
                {
                    var strategy = _plugins?.Indexes?.GetByType(index.IndexType);

                    if (strategy == null)
                    {
                        var strategyName = $"type:{index.IndexType}";
                        throw this.CreatePluginRequiredException(
                            pluginId: this.TryGetPluginIdForIndex(col, name),
                            strategyKind: strategyName,
                            operation: "DropCustomIndex",
                            collection: collection,
                            indexName: name,
                            expression: index.Expression,
                            options: null);
                    }

                    return strategy.DropIndex(snapshot, col, name);
                }

                // delete all data pages + indexes pages
                indexer.DropIndex(index);

                // remove index entry in collection page
                snapshot.CollectionPage.DeleteCollectionIndex(name);

                return true;
            });
        }

        private LiteException CreatePluginRequiredException(string pluginId, string strategyKind, string operation, string collection, string indexName, string expression, BsonDocument options)
        {
            pluginId ??= ReservedCodeRanges.VectorPluginId;
            var diagnostics = new BsonDocument
            {
                ["event"] = "plugin.index_required",
                ["operation"] = operation ?? string.Empty,
                ["collection"] = collection ?? string.Empty,
                ["index"] = indexName ?? string.Empty,
                ["expression"] = expression ?? string.Empty,
                ["strategyKind"] = strategyKind ?? string.Empty,
                ["pluginContextAvailable"] = _plugins != null,
                ["strategyRegistryAvailable"] = _plugins?.CustomIndexes != null,
                ["registeredStrategies"] = this.GetRegisteredCustomStrategies()
            };

            if (options != null && options.Count > 0)
            {
                var optionCopy = new BsonDocument();
                options.CopyTo(optionCopy);
                diagnostics["options"] = optionCopy;
            }

            var policy = _plugins?.DiagnosticPolicy ?? DefaultPluginDiagnosticPolicy.Instance;
            var exception = policy.CreateMissingPluginException(pluginId, operation ?? "PluginOperation", diagnostics);

            LOG($"custom index plugin missing: {diagnostics.ToString()}", "PLUGIN");

            return exception;
        }

        private static string TryGetPluginIdFromOptions(BsonDocument options)
        {
            if (options == null)
            {
                return null;
            }

            if (!options.TryGetValue("_pluginMetadata", out var envelope) || envelope.IsDocument == false)
            {
                return null;
            }

            var document = envelope.AsDocument;
            if (!document.TryGetValue("pluginId", out var pluginIdValue) || pluginIdValue.IsString == false)
            {
                return null;
            }

            return pluginIdValue.AsString;
        }

        private string TryGetPluginIdForIndex(CollectionPage collectionPage, string indexName)
        {
            if (collectionPage == null || string.IsNullOrWhiteSpace(indexName))
            {
                return null;
            }

            foreach (var (index, pluginId, _) in collectionPage.GetPluginIndexes())
            {
                if (string.Equals(index?.Name, indexName, StringComparison.Ordinal))
                {
                    return pluginId;
                }
            }

            return null;
        }

        private BsonArray GetRegisteredCustomStrategies()
        {
            var array = new BsonArray();
            var registered = _plugins?.CustomIndexes?.Registered;

            if (registered != null)
            {
                foreach (var descriptor in registered)
                {
                    if (descriptor?.StrategyId != null)
                    {
                        array.Add(descriptor.StrategyId);
                    }
                }
            }

            return array;
        }
    }
}

