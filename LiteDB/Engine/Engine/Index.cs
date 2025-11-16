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
                    using (var reader = new BufferReader(data.Read(pkNode.DataBlock)))
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
        /// Create a new vector index (or do nothing if already exists) for a collection/field.
        /// </summary>
        public bool EnsureVectorIndex(string collection, string name, BsonExpression expression, BsonDocument options)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (expression.Fields.Count == 0) throw new ArgumentException("Vector index expressions must reference a document field.", nameof(expression));

            if (name.Length > INDEX_NAME_MAX_LENGTH) throw LiteException.InvalidIndexName(name, collection, "MaxLength = " + INDEX_NAME_MAX_LENGTH);
            if (!name.IsWord()) throw LiteException.InvalidIndexName(name, collection, "Use only [a-Z$_]");
            if (name.StartsWith("$")) throw LiteException.InvalidIndexName(name, collection, "Index name can't start with `$`");

            var strategy = _plugins?.Indexes?.GetByKind("vector") ?? throw this.CreateVectorPluginRequiredException(
                operation: "EnsureVectorIndex",
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
                        throw this.CreateVectorPluginRequiredException(
                            operation: "DropVectorIndex",
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

        private LiteException CreateVectorPluginRequiredException(string operation, string collection, string indexName, string expression, BsonDocument options)
        {
            var diagnostics = new BsonDocument
            {
                ["event"] = "vector.plugin_required",
                ["operation"] = operation ?? string.Empty,
                ["collection"] = collection ?? string.Empty,
                ["index"] = indexName ?? string.Empty,
                ["expression"] = expression ?? string.Empty,
                ["pluginContextAvailable"] = _plugins != null,
                ["strategyRegistryAvailable"] = _plugins?.CustomIndexes != null,
                ["expectedStrategyId"] = LiteDB.VectorCompatibility.DefaultStrategyId,
                ["registeredStrategies"] = this.GetRegisteredVectorStrategies()
            };

            if (options != null && options.Count > 0)
            {
                var optionCopy = new BsonDocument();
                options.CopyTo(optionCopy);
                diagnostics["options"] = optionCopy;
            }

            var exception = LiteDB.VectorCompatibility.PluginRequired();

            try
            {
                exception.Data["VectorDiagnostics"] = diagnostics;
            }
            catch (ArgumentException)
            {
                // .NET Framework requires Exception.Data values to be serializable. Fall back to JSON text.
                exception.Data["VectorDiagnostics"] = diagnostics.ToString();
            }

            LOG($"vector plugin missing: {diagnostics.ToString()}", "PLUGIN");

            return exception;
        }

        private BsonArray GetRegisteredVectorStrategies()
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

