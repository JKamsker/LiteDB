using System;
using System.Collections.Generic;
using LiteDB.Plugins;

namespace LiteDB.Vector
{
    /// <summary>
    /// Placeholder index strategy registration that will be fleshed out as the vector pipeline migrates out of the core engine.
    /// </summary>
    internal sealed class VectorIndexStrategy : IIndexStrategy
    {
        public static VectorIndexStrategy Instance { get; } = new VectorIndexStrategy();

        private VectorIndexStrategy()
        {
        }

        public string Kind => "vector";

        public void EnsureIndex(object snapshot, string name, BsonExpression expression, BsonDocument options)
        {
            throw new NotSupportedException("Vector index creation is being migrated to the plugin infrastructure and is not yet routed through the registered strategy.");
        }

        public void DropIndex(object snapshot, string name)
        {
            throw new NotSupportedException("Vector index drop handling will be provided by the plugin strategy in a later migration step.");
        }

        public void OnDocumentUpsert(object snapshot, object collection, object dataBlock, BsonDocument document)
        {
            throw new NotSupportedException("Vector index maintenance will be delegated to the plugin strategy in a later migration step.");
        }

        public void OnDocumentDelete(object snapshot, object collection, object dataBlock)
        {
            throw new NotSupportedException("Vector index maintenance will be delegated to the plugin strategy in a later migration step.");
        }

        public IEnumerable<object> Search(object context, object spec)
        {
            throw new NotSupportedException("Vector query execution will be routed through the plugin strategy in a later migration step.");
        }
    }
}
