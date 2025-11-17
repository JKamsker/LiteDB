using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
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

                var vectorMetadata = snapshot.CollectionPage
                    .GetPluginIndexes()
                    .Where(x => string.Equals(x.PluginId, ReservedCodeRanges.VectorPluginId, StringComparison.Ordinal))
                    .ToDictionary(x => x.Index.Name, x => x.Metadata, StringComparer.Ordinal);

                foreach (var index in snapshot.CollectionPage.GetCollectionIndexes())
                {
                    var document = new BsonDocument
                    {
                        ["collection"] = collection.Key,
                        ["name"] = index.Name,
                        ["expression"] = index.Expression,
                        ["unique"] = index.Unique,
                        ["type"] = index.IndexType == 1 ? "vector" : "btree"
                    };

                    if (index.IndexType == 1 && vectorMetadata.TryGetValue(index.Name, out var metadata))
                    {
                        document["dimensions"] = (int)VectorIndexMetadataSerializer.GetDimensions(metadata);
                        document["metric"] = (int)VectorIndexMetadataSerializer.GetMetric(metadata);
                    }

                    yield return document;
                }
            }
        }
    }
}
