using System.Collections.Generic;
using LiteDB.Engine;

namespace LiteDB.Vector.Engine
{
    internal sealed class VectorIndexSearchAdapter : IVectorIndexSearchService
    {
        private readonly VectorIndexService _inner;

        public VectorIndexSearchAdapter(Snapshot snapshot, Collation collation)
        {
            _inner = new VectorIndexService(snapshot, collation);
        }

        public void Upsert(CollectionIndex index, VectorIndexMetadata metadata, BsonDocument document, PageAddress dataBlock)
        {
            _inner.Upsert(index, metadata, document, dataBlock);
        }

        public void Delete(VectorIndexMetadata metadata, PageAddress dataBlock)
        {
            _inner.Delete(metadata, dataBlock);
        }

        public IEnumerable<(BsonDocument Document, double Distance)> Search(VectorIndexMetadata metadata, float[] target, double maxDistance, int? limit)
        {
            return _inner.Search(metadata, target, maxDistance, limit);
        }

        public void Drop(VectorIndexMetadata metadata)
        {
            _inner.Drop(metadata);
        }
    }
}
