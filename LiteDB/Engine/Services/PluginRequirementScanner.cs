using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    internal sealed class PluginRequirementScanner
    {
        private readonly HeaderPage _header;
        private readonly DiskService _disk;
        private readonly WalIndexService _walIndex;
        private readonly ILitePluginContext _plugins;

        public PluginRequirementScanner(HeaderPage header, DiskService disk, WalIndexService walIndex, ILitePluginContext plugins)
        {
            _header = header ?? throw new ArgumentNullException(nameof(header));
            _disk = disk ?? throw new ArgumentNullException(nameof(disk));
            _walIndex = walIndex ?? throw new ArgumentNullException(nameof(walIndex));
            _plugins = plugins;
        }

        public IReadOnlyList<PluginRequirementRow> Scan(TransactionPages transactionPages)
        {
            var requirements = new Dictionary<string, PluginRequirementRow>(StringComparer.Ordinal);
            var loadedPluginIds = GetLoadedPluginIds(_plugins);

            var readVersion = _walIndex.CurrentReadVersion;

            using (var reader = _disk.GetReader())
            {
                foreach (var collection in _header.GetCollections())
                {
                    var scan = ScanCollection(reader, collection.Key, collection.Value, readVersion, transactionPages);

                    if (scan.PluginOwnedIndexes.Count == 0)
                    {
                        continue;
                    }

                    foreach (var index in scan.PluginOwnedIndexes)
                    {
                        scan.PluginIdsByIndexName.TryGetValue(index.Name, out var pluginId);

                        var key = !string.IsNullOrWhiteSpace(pluginId)
                            ? pluginId
                            : $"type:{index.IndexType}";

                        if (!requirements.TryGetValue(key, out var requirement))
                        {
                            requirement = new PluginRequirementRow(
                                key,
                                pluginId ?? "<unknown>");
                            requirements.Add(key, requirement);
                        }

                        requirement.AddCollection(collection.Key);
                        requirement.IndexCount++;
                        requirement.AddIndexType(index.IndexType);

                        foreach (var error in scan.Errors)
                        {
                            requirement.AddError(error);
                        }
                    }
                }
            }

            foreach (var requirement in requirements.Values)
            {
                requirement.Loaded = requirement.PluginId != "<unknown>" &&
                    loadedPluginIds.Contains(requirement.PluginId);

                requirement.StrategyAvailable = requirement.RequiredIndexTypes.All(type => _plugins?.Indexes?.GetByType(type) != null);
            }

            return requirements.Values
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();
        }

        private static HashSet<string> GetLoadedPluginIds(ILitePluginContext plugins)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            var registry = plugins?.CustomIndexes?.Registered;

            if (registry == null)
            {
                return ids;
            }

            foreach (var descriptor in registry)
            {
                if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.PluginId))
                {
                    continue;
                }

                ids.Add(descriptor.PluginId);
            }

            return ids;
        }

        private CollectionScan ScanCollection(DiskReader reader, string collectionName, uint collectionPageId, int readVersion, TransactionPages transactionPages)
        {
            var scan = new CollectionScan(collectionName);

            PageBuffer buffer = null;

            try
            {
                buffer = ReadPageBuffer(reader, collectionPageId, readVersion, transactionPages, out _);

                if (buffer == null || buffer.IsBlank())
                {
                    scan.Errors.Add(new BsonDocument
                    {
                        ["collection"] = collectionName ?? string.Empty,
                        ["message"] = "Collection page buffer was blank."
                    });

                    return scan;
                }

                if (buffer.ReadByte(BasePage.P_PAGE_TYPE) != (byte)PageType.Collection)
                {
                    scan.Errors.Add(new BsonDocument
                    {
                        ["collection"] = collectionName ?? string.Empty,
                        ["message"] = "Collection page was not a collection page."
                    });

                    return scan;
                }

                var area = buffer.Slice(PAGE_HEADER_SIZE, PAGE_SIZE - PAGE_HEADER_SIZE);

                using (var r = new BufferReader(new[] { area }, false))
                {
                    for (var i = 0; i < PAGE_FREE_LIST_SLOTS; i++)
                    {
                        r.ReadUInt32();
                    }

                    r.Skip(CollectionPage.P_INDEXES - PAGE_HEADER_SIZE - r.Position);

                    var indexCount = r.ReadByte();

                    for (var i = 0; i < indexCount; i++)
                    {
                        var indexType = ReadIndexEntry(r, out var name);

                        if (indexType != 0 && !string.IsNullOrWhiteSpace(name))
                        {
                            scan.PluginOwnedIndexes.Add(new PluginOwnedIndex(name, indexType));
                        }
                    }

                    if (r.IsEOF)
                    {
                        return scan;
                    }

                    var metadataCount = r.ReadByte();

                    for (var i = 0; i < metadataCount; i++)
                    {
                        if (!TryReadPluginMetadataEntry(r, out var indexName, out var pluginId, out var error))
                        {
                            scan.Errors.Add(new BsonDocument
                            {
                                ["collection"] = collectionName ?? string.Empty,
                                ["message"] = error ?? "Plugin metadata entry could not be parsed."
                            });

                            break;
                        }

                        if (!string.IsNullOrWhiteSpace(indexName) && !string.IsNullOrWhiteSpace(pluginId))
                        {
                            scan.PluginIdsByIndexName[indexName] = pluginId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                scan.Errors.Add(new BsonDocument
                {
                    ["collection"] = collectionName ?? string.Empty,
                    ["message"] = ex.Message
                });
            }
            finally
            {
                if (buffer != null && buffer.ShareCounter > 0)
                {
                    buffer.Release();
                }
            }

            return scan;
        }

        private static byte ReadIndexEntry(BufferReader reader, out string name)
        {
            reader.ReadByte(); // slot
            var indexType = reader.ReadByte();
            name = reader.ReadCString();

            reader.ReadCString(); // expression
            reader.ReadBoolean(); // unique
            reader.ReadPageAddress(); // head
            reader.ReadPageAddress(); // tail
            reader.ReadByte(); // reserved
            reader.ReadUInt32(); // free index page list

            return indexType;
        }

        private static bool TryReadPluginMetadataEntry(BufferReader reader, out string indexName, out string pluginId, out string error)
        {
            indexName = null;
            pluginId = null;
            error = null;

            try
            {
                indexName = reader.ReadCString();

                var marker = reader.ReadByte();

                if ((marker & 0x80) == 0)
                {
                    error = $"Plugin metadata entry '{indexName}' was stored using a legacy format.";
                    return false;
                }

                var pluginIdLength = marker & 0x7F;

                if (pluginIdLength == 0)
                {
                    error = $"Plugin metadata entry '{indexName}' did not include a plugin identifier.";
                    return false;
                }

                pluginId = reader.ReadString(pluginIdLength);

                var payloadLength = reader.ReadUInt16();

                if (payloadLength > 0)
                {
                    reader.ReadBytes(payloadLength);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private PageBuffer ReadPageBuffer(DiskReader reader, uint pageId, int readVersion, TransactionPages transactionPages, out int walVersion)
        {
            if (transactionPages != null && transactionPages.DirtyPages.TryGetValue(pageId, out var walPosition))
            {
                walVersion = readVersion;
                return reader.ReadPage(walPosition.Position, writable: false, origin: FileOrigin.Log);
            }

            var walPos = _walIndex.GetPageIndex(pageId, readVersion, out walVersion);

            if (walPos != long.MaxValue)
            {
                return reader.ReadPage(walPos, writable: false, origin: FileOrigin.Log);
            }

            walVersion = 0;
            var pagePosition = BasePage.GetPagePosition(pageId);
            return reader.ReadPage(pagePosition, writable: false, origin: FileOrigin.Data);
        }

        internal sealed class PluginRequirementRow
        {
            private readonly HashSet<string> _collections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<byte> _requiredIndexTypes = new HashSet<byte>();
            private readonly List<BsonDocument> _errors = new List<BsonDocument>();

            public PluginRequirementRow(string key, string pluginId)
            {
                Key = key ?? throw new ArgumentNullException(nameof(key));
                PluginId = string.IsNullOrWhiteSpace(pluginId) ? "<unknown>" : pluginId;
            }

            public string Key { get; }

            public string PluginId { get; }

            public int IndexCount { get; set; }

            public bool Loaded { get; set; }

            public bool StrategyAvailable { get; set; }

            public IReadOnlyCollection<byte> RequiredIndexTypes => _requiredIndexTypes;

            public IReadOnlyCollection<BsonDocument> Errors => _errors;

            public void AddCollection(string name)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _collections.Add(name);
                }
            }

            public void AddIndexType(byte indexType)
            {
                _requiredIndexTypes.Add(indexType);
            }

            public void AddError(BsonDocument error)
            {
                if (error != null)
                {
                    _errors.Add(error);
                }
            }

            public BsonDocument ToDocument()
            {
                var doc = new BsonDocument
                {
                    ["key"] = Key,
                    ["pluginId"] = PluginId,
                    ["collections"] = new BsonArray(_collections.OrderBy(x => x, StringComparer.Ordinal).Select(x => new BsonValue(x))),
                    ["indexCount"] = IndexCount,
                    ["requiredIndexTypes"] = new BsonArray(_requiredIndexTypes.OrderBy(x => x).Select(x => new BsonValue((int)x))),
                    ["loaded"] = Loaded,
                    ["strategyAvailable"] = StrategyAvailable,
                    ["errors"] = new BsonArray(_errors.Select(x => new BsonValue((object)x)))
                };

                return doc;
            }
        }

        private sealed class CollectionScan
        {
            public CollectionScan(string collectionName)
            {
                CollectionName = collectionName;
            }

            public string CollectionName { get; }

            public List<PluginOwnedIndex> PluginOwnedIndexes { get; } = new List<PluginOwnedIndex>();

            public Dictionary<string, string> PluginIdsByIndexName { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            public List<BsonDocument> Errors { get; } = new List<BsonDocument>();
        }

        private readonly struct PluginOwnedIndex
        {
            public PluginOwnedIndex(string name, byte indexType)
            {
                Name = name;
                IndexType = indexType;
            }

            public string Name { get; }

            public byte IndexType { get; }
        }
    }
}
