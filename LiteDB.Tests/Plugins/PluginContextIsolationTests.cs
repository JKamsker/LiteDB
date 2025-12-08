using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Bson;
using LiteDB.Plugins.Storage;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class PluginContextIsolationTests
    {
        private const string SharedPluginId = "Plugin.Shared";
        private const byte CustomTypeCode = 0xA1;
        private const byte PageTypeCode = 0xF1;
        private const string PageTypeName = "FakePage";

        [Fact]
        public void Registrations_with_same_code_should_be_scoped_per_database()
        {
            using var dbA = DatabaseFactory.Create(TestDatabaseType.InMemory, plugins: new[] { new IsolationPlugin(tag: 11) });
            using var dbB = DatabaseFactory.Create(TestDatabaseType.InMemory, plugins: new[] { new IsolationPlugin(tag: 22) });

            var colA = dbA.GetCollection<BsonDocument>("docs");
            var colB = dbB.GetCollection<BsonDocument>("docs");

            colA.Insert(new BsonDocument { ["_id"] = 1, ["tag"] = new TagValue(CustomTypeCode, 11) });
            colB.Insert(new BsonDocument { ["_id"] = 1, ["tag"] = new TagValue(CustomTypeCode, 22) });

            colA.FindById(1)["tag"].AsInt32.Should().Be(11);
            colB.FindById(1)["tag"].AsInt32.Should().Be(22);

            PluginContextFallbacks.Context.TryGetBsonType(CustomTypeCode, out _).Should().BeFalse("plugin registrations must not leak to the default context");
        }

        [Fact]
        public void Plugin_functions_should_not_parse_without_plugin()
        {
            using var dbWith = DatabaseFactory.Create(TestDatabaseType.InMemory, plugins: new[] { new IsolationPlugin(tag: 7) });
            var colWith = dbWith.GetCollection<BsonDocument>("docs");
            colWith.Insert(new BsonDocument { ["_id"] = 1, ["value"] = new TagValue(CustomTypeCode, 7) });

            dbWith.Services.ExpressionRegistry.Functions.Should().ContainSingle(f => f.Name == "DB_TAG");

            using var dbWithout = DatabaseFactory.Create(TestDatabaseType.InMemory);
            var colWithout = dbWithout.GetCollection<BsonDocument>("docs");
            colWithout.Insert(new BsonDocument { ["_id"] = 1, ["value"] = 123 });

            dbWithout.Services.ExpressionRegistry.Functions.Should().NotContain(f => f.Name == "DB_TAG");
        }

        [Fact]
        public void Page_factories_with_same_code_should_be_scoped_per_database()
        {
            using var dbA = DatabaseFactory.Create(TestDatabaseType.InMemory, plugins: new[] { new IsolationPlugin(tag: 1, pageCode: PageTypeCode) });
            using var dbB = DatabaseFactory.Create(TestDatabaseType.InMemory, plugins: new[] { new IsolationPlugin(tag: 2, pageCode: PageTypeCode) });

            dbA.Services.Context.PageFactories.TryGet(PageTypeCode, out var pageA).Should().BeTrue();
            dbB.Services.Context.PageFactories.TryGet(PageTypeCode, out var pageB).Should().BeTrue();

            pageA.PluginId.Should().Be(SharedPluginId);
            pageB.PluginId.Should().Be(SharedPluginId);
            pageA.Should().NotBeSameAs(pageB);

            PluginContextFallbacks.Context.PageFactories.TryGet(PageTypeCode, out _).Should().BeFalse();
        }

        private sealed class IsolationPlugin : ILitePlugin
        {
            private readonly int _tag;
            private readonly byte _pageCode;

            public IsolationPlugin(int tag, byte pageCode = 0)
            {
                _tag = tag;
                _pageCode = pageCode;
            }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                var descriptor = new CustomBsonTypeDescriptor(
                    SharedPluginId,
                    CustomTypeCode,
                    name: $"tag-{_tag}",
                    calculateSize: _ => sizeof(int),
                    serializer: (writer, value) => ((BufferWriter)writer).Write(value.AsInt32),
                    deserializer: reader => new TagValue(CustomTypeCode, ((BufferReader)reader).ReadInt32()),
                    jsonFormatter: value => value.AsInt32.ToString());

                context.RegisterBsonType(descriptor);

                context.Expressions.RegisterFunction(
                    "DB_TAG",
                    new Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue>((_, __, ___, ____) => _tag),
                    BsonExpressionType.Call,
                    convertScalarLeftToEnumerable: false,
                    isScalarResult: true);

                if (_pageCode != 0)
                {
                    context.RegisterPageFactory(new PageFactoryRegistration(
                        pluginId: SharedPluginId,
                        pageType: PageTypeName,
                        numericCode: _pageCode,
                        compatibilityRange: ">=8.0",
                        factory: ctx => new object()));
                }
            }
        }

        private sealed class TagValue : BsonValue
        {
            public TagValue(byte typeCode, int tag)
                : base((BsonType)typeCode, tag)
            {
            }

            public int Tag => (int)this.RawValue;

            internal override int GetBytesCount(bool recalc) => sizeof(int);

            internal override bool TryWriteJson(JsonWriter writer)
            {
                writer.Serialize(new BsonValue(this.Tag));
                return true;
            }
        }
    }
}
