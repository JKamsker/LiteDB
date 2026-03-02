using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;

using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Implement a full rebuild database. Engine will be closed and re-created in another instance.
        /// A backup copy will be created with -backup extention. All data will be readed and re created in another database
        /// After run, will re-open database
        /// </summary>
        public long Rebuild(RebuildOptions options)
        {
            if (string.IsNullOrEmpty(_settings.Filename)) return 0; // works only with os file

            if (options == null) throw new ArgumentNullException(nameof(options));

            this.EnsurePluginAssetsAllowed(options);

            this.Close();

            // run build service
            var rebuilder = new RebuildService(_settings, _plugins);

            // return how many bytes of diference from original/rebuild version
            var diff = rebuilder.Rebuild(options);

            // re-open engine
            this.Open();

            _state.Disposed = false;

            return diff;
        }

        private void EnsurePluginAssetsAllowed(RebuildOptions options)
        {
            if (options != null && options.DropOrphanedPluginIndexes)
            {
                return;
            }

            var scanner = new PluginRequirementScanner(_header, _disk, _walIndex, _plugins);
            var requirements = scanner.Scan(transactionPages: null);
            var missing = requirements.Where(x => x.StrategyAvailable == false).ToArray();

            if (missing.Length == 0)
            {
                return;
            }

            var diagnostics = new BsonDocument
            {
                ["event"] = "plugin.rebuild_preflight_failed",
                ["requirements"] = new BsonArray(requirements.Select(x => x.ToDocument())),
                ["missingCount"] = missing.Length
            };

            var pluginId = missing.Select(x => x.PluginId).FirstOrDefault(x => !string.Equals(x, "<unknown>", StringComparison.Ordinal));
            var policy = _plugins?.DiagnosticPolicy ?? DefaultPluginDiagnosticPolicy.Instance;

            throw policy.CreateMissingPluginException(pluginId, "Rebuild", diagnostics);
        }

        /// <summary>
        /// Implement a full rebuild database. A backup copy will be created with -backup extention. All data will be readed and re created in another database
        /// </summary>
        public long Rebuild()
        {
            var collation = new Collation(this.Pragma(Pragmas.COLLATION));
            var password = _settings.Password;

            return this.Rebuild(new RebuildOptions { Password = password, Collation = collation });
        }

        /// <summary>
        /// Fill current database with data inside file reader - run inside a transacion
        /// </summary>
        internal void RebuildContent(IFileReader reader, RebuildOptions options)
        {
            // begin transaction and get TransactionID
            var transaction = _monitor.GetTransaction(true, false, out _);

            try
            {
                foreach (var collection in reader.GetCollections())
                {
                    // get snapshot, indexer and data services
                    var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, true);
                    var indexer = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);
                    var data = new DataService(snapshot, _disk.MAX_ITEMS_COUNT);
                    var pluginStrategies = _plugins?.Indexes?.All ?? Array.Empty<IIndexStrategy>();

                    // get all documents from current collection
                    var docs = reader.GetDocuments(collection);

                    // insert one-by-one
                    foreach (var doc in docs)
                    {
                        transaction.Safepoint();

                        this.InsertDocument(snapshot, doc, BsonAutoId.ObjectId, indexer, data, pluginStrategies);
                    }

                    // first create all user indexes (exclude _id index)
                    foreach (var index in reader.GetIndexes(collection))
                    {
                        if (this.TryRebuildPluginIndex(collection, index, options))
                        {
                            continue;
                        }

                        this.EnsureIndex(
                            collection,
                            index.Name,
                            index.BsonExpr,
                            index.Unique);
                    }
                }

                transaction.Commit();

                _monitor.ReleaseTransaction(transaction);
            }
            catch (Exception ex)
            {
                this.Close(ex);

                throw;
            }
        }

    private bool TryRebuildPluginIndex(string collection, IndexInfo index, RebuildOptions options)
    {
        if (index == null)
        {
            return false;
        }

        var isPluginOwned = index.IndexType != 0 || index.PluginMetadata != null || !string.IsNullOrWhiteSpace(index.PluginId);

        if (isPluginOwned == false)
        {
            return false;
        }

        try
        {
            if (index.PluginMetadata == null || string.IsNullOrWhiteSpace(index.PluginId))
            {
                throw this.CreatePluginRequiredException(
                    pluginId: index.PluginId,
                    strategyKind: index.PluginId ?? $"type:{index.IndexType}",
                    operation: "RebuildCustomIndex",
                    collection: collection,
                    indexName: index.Name,
                    expression: index.Expression,
                    options: null);
            }

            var pluginId = index.PluginId;
            var pluginContext = _plugins;
            var metadataRegistry = pluginContext?.IndexMetadata;
            var strategyRegistry = pluginContext?.CustomIndexes;

            if (metadataRegistry == null || strategyRegistry == null || string.IsNullOrWhiteSpace(pluginId))
            {
                throw this.CreatePluginRequiredException(
                    pluginId: pluginId,
                    strategyKind: pluginId ?? $"type:{index.IndexType}",
                    operation: "RebuildCustomIndex",
                    collection: collection,
                    indexName: index.Name,
                    expression: index.Expression,
                    options: null);
            }

            var metadataDescriptor = this.ResolvePluginMetadataDescriptor(metadataRegistry, pluginId, index.PluginIndexKind);

            if (metadataDescriptor == null)
            {
                throw this.CreatePluginRequiredException(
                    pluginId: pluginId,
                    strategyKind: pluginId,
                    operation: "RebuildCustomIndex",
                    collection: collection,
                    indexName: index.Name,
                    expression: index.Expression,
                    options: null);
            }

            var metadataDocument = index.PluginMetadataDocument;

            if (metadataDocument == null)
            {
                try
                {
                    metadataDocument = metadataDescriptor.Deserialize(index.PluginMetadata) ?? new BsonDocument();
                }
                catch (Exception ex)
                {
                    throw new LiteException(LiteException.PLUGIN_REQUIRED, ex, $"Failed to deserialize metadata for index '{collection}.{index.Name}' owned by plugin '{pluginId}'.");
                }
            }

            var indexOptions = this.BuildPluginIndexOptions(metadataDescriptor, metadataDocument);
            var strategyKind = this.ResolveStrategyKind(index.IndexType);

            if (string.IsNullOrWhiteSpace(strategyKind))
            {
                throw this.CreatePluginRequiredException(
                    pluginId: pluginId,
                    strategyKind: pluginId,
                    operation: "RebuildCustomIndex",
                    collection: collection,
                    indexName: index.Name,
                    expression: index.Expression,
                    options: indexOptions);
            }

            this.EnsureCustomIndex(
                collection,
                index.Name,
                strategyKind,
                index.BsonExpr,
                indexOptions);

            var strategyDescriptor = this.ResolveCustomIndexDescriptor(strategyRegistry, pluginId);
            strategyDescriptor?.RebuildStrategy?.Invoke(new CustomIndexRebuildContext(this, _plugins));

            return true;
        }
        catch (Exception ex) when (options?.DropOrphanedPluginIndexes == true)
        {
            var owner = string.IsNullOrWhiteSpace(index.PluginId) ? $"<unknown:type:{index.IndexType}>" : index.PluginId;

            options.Errors.Add(new FileReaderError
            {
                Origin = FileOrigin.Data,
                PageType = PageType.Index,
                Collection = collection,
                Message = $"Dropped orphaned plugin index '{collection}.{index.Name}' owned by '{owner}'. {ex.Message}",
                Exception = ex
            });

            return true;
        }
    }

    private PluginIndexMetadataDescriptor ResolvePluginMetadataDescriptor(IPluginIndexMetadataRegistry registry, string pluginId, string indexKind)
    {
        if (registry == null || string.IsNullOrWhiteSpace(pluginId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(indexKind) &&
            registry.TryGet(indexKind, out var descriptor) &&
            string.Equals(descriptor.PluginId, pluginId, StringComparison.Ordinal))
        {
            return descriptor;
        }

        PluginIndexMetadataDescriptor match = null;
        var registered = registry.Registered;

        if (registered == null)
        {
            return null;
        }

        foreach (var candidate in registered)
        {
            if (candidate == null || !string.Equals(candidate.PluginId, pluginId, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new LiteException(0, $"Multiple metadata descriptors registered for plugin '{pluginId}'. Unable to resolve rebuild metadata.");
            }

            match = candidate;
        }

        return match;
    }

    private CustomIndexStrategyDescriptor ResolveCustomIndexDescriptor(ICustomIndexStrategyRegistry registry, string pluginId)
    {
        if (registry == null || string.IsNullOrWhiteSpace(pluginId))
        {
            return null;
        }

        CustomIndexStrategyDescriptor match = null;
        var registered = registry.Registered;

        if (registered == null)
        {
            return null;
        }

        foreach (var candidate in registered)
        {
            if (candidate == null || !string.Equals(candidate.PluginId, pluginId, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new LiteException(0, $"Multiple custom index strategies registered for plugin '{pluginId}'. Unable to determine rebuild delegate.");
            }

            match = candidate;
        }

        return match;
    }

    private BsonDocument BuildPluginIndexOptions(PluginIndexMetadataDescriptor descriptor, BsonDocument metadata)
    {
        var options = new BsonDocument();
        var payload = new BsonDocument();

        if (metadata != null)
        {
            metadata.CopyTo(options);
            metadata.CopyTo(payload);
        }

        var envelope = new BsonDocument
        {
            ["pluginId"] = descriptor.PluginId
        };

        if (!string.IsNullOrWhiteSpace(descriptor.IndexKind))
        {
            envelope["indexKind"] = descriptor.IndexKind;
        }

        envelope["payload"] = payload;
        options["_pluginMetadata"] = envelope;

        return options;
    }

    private string ResolveStrategyKind(byte indexType)
    {
        var strategy = _plugins?.Indexes?.GetByType(indexType);
        return strategy?.Kind;
    }
    }
}
