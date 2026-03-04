using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Indexing;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class DefaultPluginContextRegistryTests
    {
        private sealed class TestIndexStrategy : IIndexStrategy
        {
            public TestIndexStrategy(string kind, byte indexTypeCode)
            {
                Kind = kind;
                IndexTypeCode = indexTypeCode;
            }

            public string Kind { get; }

            public byte IndexTypeCode { get; }

            public bool EnsureIndex(object snapshot, object collection, string name, BsonExpression expression, BsonDocument options) => throw new NotSupportedException();

            public bool DropIndex(object snapshot, object collection, string name) => throw new NotSupportedException();

            public void OnDocumentUpsert(object snapshot, object collection, object dataBlock, BsonDocument document) => throw new NotSupportedException();

            public void OnDocumentDelete(object snapshot, object collection, object dataBlock) => throw new NotSupportedException();
        }

        [Fact]
        public void IndexRegistry_should_throw_on_duplicate_IndexTypeCode()
        {
            var context = new DefaultPluginContext(new ConnectionString(), null, null);

            context.Indexes.Register(new TestIndexStrategy("a", 10));

            Action act = () => context.Indexes.Register(new TestIndexStrategy("b", 10));

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void IndexRegistry_should_throw_on_duplicate_Kind()
        {
            var context = new DefaultPluginContext(new ConnectionString(), null, null);

            context.Indexes.Register(new TestIndexStrategy("dup", 10));

            Action act = () => context.Indexes.Register(new TestIndexStrategy("Dup", 11));

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void PluginIndexMetadataRegistry_should_throw_on_duplicate_IndexKind()
        {
            var context = new DefaultPluginContext(new ConnectionString(), null, null);

            var descriptor1 = new PluginIndexMetadataDescriptor(
                pluginId: "p1",
                indexKind: "kind",
                serialize: _ => Array.Empty<byte>(),
                deserialize: _ => new BsonDocument());

            var descriptor2 = new PluginIndexMetadataDescriptor(
                pluginId: "p2",
                indexKind: "kind",
                serialize: _ => Array.Empty<byte>(),
                deserialize: _ => new BsonDocument());

            context.IndexMetadata.Register(descriptor1);

            Action act = () => context.IndexMetadata.Register(descriptor2);

            act.Should().Throw<InvalidOperationException>();
        }
    }
}
