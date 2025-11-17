using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    internal class CollectionPage : BasePage
    {
        #region Buffer Field Positions

        public const int P_INDEXES = 96; // 96-8192 (64 + 32 header = 96)
        public const int P_INDEXES_COUNT = PAGE_SIZE - P_INDEXES; // 8096

        #endregion

        /// <summary>
        /// Free data page linked-list (N lists for different range of FreeBlocks)
        /// </summary>
        public uint[] FreeDataPageList { get; } = new uint[PAGE_FREE_LIST_SLOTS];

        /// <summary>
        /// All indexes references for this collection
        /// </summary>
        private readonly Dictionary<string, CollectionIndex> _indexes = new Dictionary<string, CollectionIndex>();
        private readonly Dictionary<string, PluginIndexMetadataEntry> _pluginIndexes = new Dictionary<string, PluginIndexMetadataEntry>();

        private sealed class PluginIndexMetadataEntry
        {
            public PluginIndexMetadataEntry(string pluginId, string indexKind, byte[] payload)
            {
                if (string.IsNullOrWhiteSpace(pluginId))
                {
                    throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
                }

                PluginId = pluginId;
                IndexKind = indexKind;
                Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            }

            public string PluginId { get; }

            public string IndexKind { get; private set; }

            public byte[] Payload { get; }

            public void EnsureIndexKind(string value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    IndexKind ??= value;
                }
            }
        }

        public CollectionPage(PageBuffer buffer, uint pageID)
            : base(buffer, pageID, PageType.Collection)
        {
            for(var i = 0; i < PAGE_FREE_LIST_SLOTS; i++)
            {
                this.FreeDataPageList[i] = uint.MaxValue;
            }
        }

        public CollectionPage(PageBuffer buffer)
            : base(buffer)
        {
            ENSURE(this.PageType == PageType.Collection, "page type must be collection page");

            if (this.PageType != PageType.Collection) throw LiteException.InvalidPageType(PageType.Collection, this);

            // create new buffer area to store BsonDocument indexes
            var area = _buffer.Slice(PAGE_HEADER_SIZE, PAGE_SIZE - PAGE_HEADER_SIZE);

            using (var r = new BufferReader(new[] { area }, false))
            {
                // read position for FreeDataPage and FreeIndexPage
                for(var i = 0; i < PAGE_FREE_LIST_SLOTS; i++)
                {
                    this.FreeDataPageList[i] = r.ReadUInt32();
                }

                // skip reserved area
                r.Skip(P_INDEXES - PAGE_HEADER_SIZE - r.Position);

                var count = r.ReadByte(); // 1 byte

                for(var i = 0; i < count; i++)
                {
                    var index = new CollectionIndex(r);

                    _indexes[index.Name] = index;
                }

                var metadataCount = r.ReadByte();

                for (var i = 0; i < metadataCount; i++)
                {
                    var name = r.ReadCString();
                    var marker = r.ReadByte();

                    if ((marker & 0x80) == 0)
                    {
                        var legacy = new byte[VectorIndexMetadataSerializer.MetadataLength];
                        legacy[0] = marker;
                        r.Read(legacy, 1, VectorIndexMetadataSerializer.MetadataLength - 1);

                        _pluginIndexes[name] = new PluginIndexMetadataEntry(
                            ReservedCodeRanges.VectorPluginId,
                            ReservedCodeRanges.VectorIndexKind,
                            legacy);

                        continue;
                    }

                    var pluginIdLength = marker & 0x7F;

                    if (pluginIdLength == 0)
                    {
                        throw new LiteException(0, "Plugin metadata entries must include a plugin identifier.");
                    }

                    var pluginId = r.ReadString(pluginIdLength);
                    var payloadLength = r.ReadUInt16();
                    var payload = r.ReadBytes(payloadLength);

                    _pluginIndexes[name] = new PluginIndexMetadataEntry(pluginId, null, payload);
                }
            }
        }

        public override PageBuffer UpdateBuffer()
        {
            // if page was deleted, do not write in content area (must keep with 0 only)
            if (this.PageType == PageType.Empty) return base.UpdateBuffer();

            var area = _buffer.Slice(PAGE_HEADER_SIZE, PAGE_SIZE - PAGE_HEADER_SIZE);

            using (var w = new BufferWriter(area))
            {
                // read position for FreeDataPage and FreeIndexPage
                for (var i = 0; i < PAGE_FREE_LIST_SLOTS; i++)
                {
                    w.Write(this.FreeDataPageList[i]);
                }

                // skip reserved area (indexes starts at position 96)
                w.Skip(P_INDEXES - PAGE_HEADER_SIZE - w.Position);

                w.Write((byte)_indexes.Count); // 1 byte

                foreach (var index in _indexes.Values)
                {
                    index.UpdateBuffer(w);
                }

                w.Write((byte)_pluginIndexes.Count);

                foreach (var pair in _pluginIndexes)
                {
                    w.WriteCString(pair.Key);

                    var pluginIdBytes = StringEncoding.UTF8.GetBytes(pair.Value.PluginId);

                    if (pluginIdBytes.Length >= 0x80)
                    {
                        throw new LiteException(0, $"Plugin identifier '{pair.Value.PluginId}' exceeds the supported length (127 bytes).");
                    }

                    var payload = pair.Value.Payload ?? Array.Empty<byte>();

                    if (payload.Length > ushort.MaxValue)
                    {
                        throw new LiteException(0, $"Plugin metadata payload for index '{pair.Key}' exceeds the supported length ({ushort.MaxValue} bytes).");
                    }

                    var marker = (byte)(pluginIdBytes.Length | 0x80);

                    w.Write(marker);
                    w.Write(pluginIdBytes);
                    w.Write((ushort)payload.Length);
                    w.Write(payload);
                }
            }

            return base.UpdateBuffer();
        }

        /// <summary>
        /// Get PK index
        /// </summary>
        public CollectionIndex PK { get { return _indexes["_id"]; } }

        /// <summary>
        /// Get index from index name (index name is case sensitive) - returns null if not found
        /// </summary>
        public CollectionIndex GetCollectionIndex(string name)
        {
            if (_indexes.TryGetValue(name, out var index))
            {
                return index;
            }

            return null;
        }

        /// <summary>
        /// Get all indexes in this collection page
        /// </summary>
        public ICollection<CollectionIndex> GetCollectionIndexes()
        {
            return _indexes.Values;
        }

        /// <summary>
        /// Get all collections array based on slot number
        /// </summary>
        public CollectionIndex[] GetCollectionIndexesSlots()
        {
            var indexes = new CollectionIndex[_indexes.Max(x => x.Value.Slot) + 1];

            foreach (var index in _indexes.Values)
            {
                indexes[index.Slot] = index;
            }

            return indexes;
        }

        private int GetSerializedLength(int additionalIndexLength, int additionalMetadataLength)
        {
            var length = 1 + _indexes.Sum(x => CollectionIndex.GetLength(x.Value)) + additionalIndexLength;

            length += 1 + _pluginIndexes.Sum(x => GetPluginMetadataLength(x.Key, x.Value)) + additionalMetadataLength;

            return length;
        }

        private static int GetPluginMetadataLength(string name, PluginIndexMetadataEntry entry)
        {
            return GetPluginMetadataLength(name, entry.PluginId, entry.Payload?.Length ?? 0);
        }

        private static int GetPluginMetadataLength(string name, string pluginId, int payloadLength)
        {
            return
                StringEncoding.UTF8.GetByteCount(name) + 1 + // name + \0
                1 + // pluginId length marker
                StringEncoding.UTF8.GetByteCount(pluginId) +
                2 + // payload length
                payloadLength;
        }

        public IEnumerable<(CollectionIndex Index, string PluginId, byte[] Metadata)> GetPluginIndexes()
        {
            foreach (var pair in _pluginIndexes)
            {
                if (_indexes.TryGetValue(pair.Key, out var index))
                {
                    yield return (index, pair.Value.PluginId, pair.Value.Payload);
                }
            }
        }

        public IEnumerable<(CollectionIndex Index, PluginIndexMetadata Metadata)> GetPluginIndexes(IPluginIndexMetadataRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            foreach (var pair in _pluginIndexes)
            {
                if (!_indexes.TryGetValue(pair.Key, out var index))
                {
                    continue;
                }

                var descriptor = this.ResolveDescriptor(pair.Value, registry);

                if (descriptor == null)
                {
                    continue;
                }

                pair.Value.EnsureIndexKind(descriptor.IndexKind);

                yield return (index, new PluginIndexMetadata(pair.Value.PluginId, descriptor.IndexKind, pair.Value.Payload));
            }
        }

        private PluginIndexMetadataDescriptor ResolveDescriptor(PluginIndexMetadataEntry entry, IPluginIndexMetadataRegistry registry)
        {
            if (!string.IsNullOrEmpty(entry.IndexKind) &&
                registry.TryGet(entry.IndexKind, out var descriptor) &&
                string.Equals(descriptor.PluginId, entry.PluginId, StringComparison.Ordinal))
            {
                return descriptor;
            }

            PluginIndexMetadataDescriptor match = null;
            var registered = registry.Registered;

            if (registered != null)
            {
                foreach (var candidate in registered)
                {
                    if (candidate == null || !string.Equals(candidate.PluginId, entry.PluginId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        throw new LiteException(0, $"Multiple metadata descriptors registered for plugin '{entry.PluginId}'. Unable to disambiguate index metadata.");
                    }

                    match = candidate;
                }
            }

            if (match == null && entry.IndexKind != null && registry.TryGet(entry.IndexKind, out var fallback))
            {
                match = fallback;
            }

            return match;
        }

        public byte[] GetPluginIndexMetadata(string name)
        {
            return _pluginIndexes.TryGetValue(name, out var metadata) ? metadata.Payload : null;
        }

        /// <summary>
        /// Insert new index inside this collection page
        /// </summary>
        public CollectionIndex InsertCollectionIndex(string name, string expr, bool unique, IExpressionRegistry registry = null)
        {
            if (_indexes.ContainsKey(name) || _pluginIndexes.ContainsKey(name))
            {
                throw LiteException.IndexAlreadyExist(name);
            }

            var totalLength = this.GetSerializedLength(CollectionIndex.GetLength(name, expr), 0);

            if (_indexes.Count == 255 || totalLength >= P_INDEXES_COUNT) throw new LiteException(0, $"This collection has no more space for new indexes");

            var slot = (byte)(_indexes.Count == 0 ? 0 : (_indexes.Max(x => x.Value.Slot) + 1));

            var index = new CollectionIndex(slot, 0, name, expr, unique);
            index.BindExpressionRegistry(registry);

            _indexes[name] = index;

            this.IsDirty = true;

            return index;
        }

        public (CollectionIndex Index, byte[] Metadata) InsertPluginIndex(
            string name,
            string expr,
            byte indexType,
            bool unique,
            PluginIndexMetadataDescriptor descriptor,
            BsonDocument metadataDocument,
            IExpressionRegistry registry = null)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (metadataDocument == null)
            {
                throw new ArgumentNullException(nameof(metadataDocument));
            }

            if (_indexes.ContainsKey(name) || _pluginIndexes.ContainsKey(name))
            {
                throw LiteException.IndexAlreadyExist(name);
            }

            var slot = (byte)(_indexes.Count == 0 ? 0 : (_indexes.Max(x => x.Value.Slot) + 1));

            var hydratedMetadata = new BsonDocument();
            metadataDocument.CopyTo(hydratedMetadata);
            hydratedMetadata["slot"] = (int)slot;

            byte[] payload;

            try
            {
                payload = descriptor.Serialize(hydratedMetadata) ?? throw new LiteException(0, $"Plugin '{descriptor.PluginId}' did not return metadata for index '{name}'.");
            }
            catch (Exception ex)
            {
                throw new LiteException(0, $"Plugin '{descriptor.PluginId}' failed to serialize metadata for index '{name}'.", ex);
            }

            var pluginIdLength = StringEncoding.UTF8.GetByteCount(descriptor.PluginId);

            if (pluginIdLength >= 0x80)
            {
                throw new LiteException(0, $"Plugin identifier '{descriptor.PluginId}' exceeds the supported length (127 bytes).");
            }

            if (payload.Length > ushort.MaxValue)
            {
                throw new LiteException(0, $"Plugin metadata payload for index '{name}' exceeds the supported length ({ushort.MaxValue} bytes).");
            }

            var additionalMetadataLength = GetPluginMetadataLength(name, descriptor.PluginId, payload.Length);
            var totalLength = this.GetSerializedLength(CollectionIndex.GetLength(name, expr), additionalMetadataLength);

            if (_indexes.Count == 255 || totalLength >= P_INDEXES_COUNT)
            {
                throw new LiteException(0, $"This collection has no more space for new indexes");
            }

            var index = new CollectionIndex(slot, indexType, name, expr, unique);
            index.BindExpressionRegistry(registry);

            _indexes[name] = index;
            _pluginIndexes[name] = new PluginIndexMetadataEntry(descriptor.PluginId, descriptor.IndexKind, payload);

            this.IsDirty = true;

            return (index, payload);
        }

        /// <summary>
        /// Return index instance and mark as updatable
        /// </summary>
        public CollectionIndex UpdateCollectionIndex(string name)
        {
            this.IsDirty = true;

            return _indexes[name];
        }

        /// <summary>
        /// Remove index reference in this page
        /// </summary>
        public void DeleteCollectionIndex(string name)
        {
            _indexes.Remove(name);
            _pluginIndexes.Remove(name);

            this.IsDirty = true;
        }

        public void BindExpressionRegistry(IExpressionRegistry registry)
        {
            foreach (var index in _indexes.Values)
            {
                index.BindExpressionRegistry(registry);
            }
        }

    }
}
