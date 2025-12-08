using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        private IEnumerable<BsonDocument> SysIndexes()
        {
            // get any transaction from current thread ID
            var transaction = _monitor.GetThreadTransaction();

            foreach (var collection in _header.GetCollections())
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Read, collection.Key, false);

                var pluginMetadata = snapshot.CollectionPage
                    .GetPluginIndexes()
                    .ToDictionary(x => x.Index.Name, x => (x.PluginId, x.Metadata), StringComparer.Ordinal);

                foreach (var index in snapshot.CollectionPage.GetCollectionIndexes())
                {
                    var isCustom = pluginMetadata.TryGetValue(index.Name, out var metadataEntry);

                    var document = new BsonDocument
                    {
                        ["collection"] = collection.Key,
                        ["name"] = index.Name,
                        ["expression"] = index.Expression,
                        ["unique"] = index.Unique,
                        ["type"] = isCustom ? "custom" : "btree"
                    };

                    if (isCustom)
                    {
                        if (!string.IsNullOrWhiteSpace(metadataEntry.PluginId))
                        {
                            document["pluginId"] = metadataEntry.PluginId;
                        }

                        if (metadataEntry.Metadata != null)
                        {
                            document["pluginMetadataLength"] = metadataEntry.Metadata.Length;
                        }
                    }

                    yield return document;
                }
            }
        }
    }
}
