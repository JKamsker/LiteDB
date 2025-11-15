using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Vector.Engine;

namespace LiteDB.Vector.Query
{
    /// <summary>
    /// Index implementation that executes vector similarity searches using the plugin's vector index service.
    /// </summary>
    internal sealed class VectorIndexQuery : LiteDB.Engine.Index, IDocumentLookup
    {
        private readonly Snapshot _snapshot;
        private readonly CollectionIndex _index;
        private readonly VectorIndexMetadata _metadata;
        private readonly float[] _target;
        private readonly double _maxDistance;
        private readonly int? _limit;
        private readonly Collation _collation;

        private readonly Dictionary<PageAddress, BsonDocument> _cache = new Dictionary<PageAddress, BsonDocument>();

        public VectorIndexQuery(
            string name,
            Snapshot snapshot,
            CollectionIndex index,
            VectorIndexMetadata metadata,
            float[] target,
            double maxDistance,
            int? limit,
            Collation collation)
            : base(name, global::LiteDB.Query.Ascending)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _maxDistance = maxDistance;
            _limit = limit;
            _collation = collation;
        }

        public string Expression => _index.Expression;

        public override uint GetCost(CollectionIndex index)
        {
            return 1;
        }

        public override IEnumerable<IndexNode> Execute(IndexService indexer, CollectionIndex index)
        {
            throw new NotSupportedException();
        }

        public override IEnumerable<IndexNode> Run(CollectionPage col, IndexService indexer)
        {
            _cache.Clear();

            var service = new VectorIndexService(_snapshot, _collation);
            var results = service.Search(_metadata, _target, _maxDistance, _limit).ToArray();

            foreach (var result in results)
            {
                var rawId = result.Document.RawId;

                if (rawId.IsEmpty)
                {
                    continue;
                }

                _cache[rawId] = result.Document;
                yield return new IndexNode(result.Document);
            }
        }

        public BsonDocument Load(IndexNode node)
        {
            if (node.Key is BsonDocument document)
            {
                return document;
            }

            throw new InvalidOperationException("Vector index query expected document key payload.");
        }

        public BsonDocument Load(PageAddress rawId)
        {
            if (_cache.TryGetValue(rawId, out var document))
            {
                return document;
            }

            throw new KeyNotFoundException("Vector index query cache miss for requested document.");
        }

        public override string ToString()
        {
            return "VECTOR INDEX SEARCH";
        }
    }
}
