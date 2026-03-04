using System;
using FluentAssertions;
using LiteDB.Plugins;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class IndexRegistryValidationTests
    {
        [Fact]
        public void Index_registry_should_refuse_plugin_strategies_with_IndexTypeCode_zero()
        {
            var plugin = new ZeroIndexTypePlugin();

            Action act = () =>
            {
                using var db = new LiteDatabaseBuilder()
                    .UseInMemory()
                    .UsePlugin(plugin)
                    .Build();
            };

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*IndexTypeCode*0*");
        }

        private sealed class ZeroIndexTypePlugin : ILitePlugin
        {
            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.Indexes.Register(new ZeroIndexTypeStrategy());
            }
        }

        private sealed class ZeroIndexTypeStrategy : IIndexStrategy
        {
            public string Kind => "ZERO";

            public byte IndexTypeCode => 0;

            public bool EnsureIndex(object snapshot, object collection, string name, BsonExpression expression, BsonDocument options) => false;

            public bool DropIndex(object snapshot, object collection, string name) => false;

            public void OnDocumentUpsert(object snapshot, object collection, object dataBlock, BsonDocument document)
            {
            }

            public void OnDocumentDelete(object snapshot, object collection, object dataBlock)
            {
            }
        }
    }
}

