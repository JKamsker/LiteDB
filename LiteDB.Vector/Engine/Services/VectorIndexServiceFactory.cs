using System;
using System.Collections.Generic;
using LiteDB.Engine;

namespace LiteDB.Vector.Engine
{
    internal interface IVectorIndexSearchService
    {
        void Upsert(CollectionIndex index, VectorIndexMetadata metadata, BsonDocument document, PageAddress dataBlock);

        void Delete(VectorIndexMetadata metadata, PageAddress dataBlock);

        IEnumerable<(BsonDocument Document, double Distance)> Search(VectorIndexMetadata metadata, float[] target, double maxDistance, int? limit);

        void Drop(VectorIndexMetadata metadata);
    }

    internal static class VectorIndexServiceFactory
    {
        private static Func<Snapshot, Collation, IVectorIndexSearchService>? _factory;

        public static void Register(Func<Snapshot, Collation, IVectorIndexSearchService> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static IVectorIndexSearchService Create(Snapshot snapshot, Collation collation)
        {
            if (_factory == null)
            {
                throw new LiteException(0, "Vector indexing requires the LiteDB.Vector extension package. Install via: dotnet add package LiteDB.Vector");
            }

            return _factory(snapshot, collation);
        }
    }
}
