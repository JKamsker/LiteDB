using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Plugins.Bson;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class PluginContextIsolationTests
    {
        private const byte CustomTypeCode = 0xA1;
        private const string SharedPluginId = "Plugin.Shared";

        [Fact]
        public void Custom_bson_types_remain_scoped_to_each_database_instance()
        {
            using var fileA = new TempFile();
            using var fileB = new TempFile();

            using var dbA = DatabaseFactory.Create(TestDatabaseType.Disk, fileA.Filename, plugins: new[] { new IsolationPlugin(SharedPluginId, CustomTypeCode, tag: 11) });
            using var dbB = DatabaseFactory.Create(TestDatabaseType.Disk, fileB.Filename, plugins: new[] { new IsolationPlugin(SharedPluginId, CustomTypeCode, tag: 22) });

            var docsA = dbA.GetCollection<BsonDocument>("docs");
            docsA.Insert(new BsonDocument { ["_id"] = 1, ["tag"] = new TagValue(CustomTypeCode, 11) });

            var docsB = dbB.GetCollection<BsonDocument>("docs");
            docsB.Insert(new BsonDocument { ["_id"] = 1, ["tag"] = new TagValue(CustomTypeCode, 22) });

            var reloadedA = (TagValue)docsA.FindById(1)["tag"];
            var reloadedB = (TagValue)docsB.FindById(1)["tag"];

            reloadedA.Tag.Should().Be(11);
            reloadedB.Tag.Should().Be(22);

            LiteDatabaseServices.Default.Context.TryGetBsonType(CustomTypeCode, out _).Should().BeFalse("plugins must not mutate the default/global context");

            Action readWithoutPlugin = () =>
            {
                using var vanilla = DatabaseFactory.Create(TestDatabaseType.Disk, fileA.Filename);
                vanilla.GetCollection<BsonDocument>("docs").FindAll().ToList();
            };

            readWithoutPlugin.Should().Throw<Exception>("opening a plugin-authored database without the plugin should fail");
        }

        [Fact]
        public void Expression_and_type_registries_do_not_leak_across_databases()
        {
            using var dbWithPlugin = DatabaseFactory.Create(TestDatabaseType.InMemory, plugins: new[] { new IsolationPlugin(SharedPluginId, CustomTypeCode, tag: 7) });
            var collection = dbWithPlugin.GetCollection<BsonDocument>("docs");
            collection.Insert(new BsonDocument { ["_id"] = 1, ["value"] = new TagValue(CustomTypeCode, 7) });

            collection
                .Query()
                .Where("DB_TAG() = 7")
                .Count()
                .Should().Be(1, "plugin-provided expressions must be resolved through the owning database registry");

            using var dbWithoutPlugin = DatabaseFactory.Create(TestDatabaseType.InMemory);
            var vanillaCollection = dbWithoutPlugin.GetCollection<BsonDocument>("docs");
            vanillaCollection.Insert(new BsonDocument { ["_id"] = 1, ["value"] = 123 });

            Action act = () => vanillaCollection.Query().Where("DB_TAG() = 7").ToList();

            act.Should()
                .Throw<Exception>()
                .Which.Message.Should().ContainEquivalentOf("DB_TAG");
        }

        private sealed class IsolationPlugin : ILitePlugin
        {
            private readonly string _pluginId;
            private readonly byte _typeCode;
            private readonly int _tag;

            public IsolationPlugin(string pluginId, byte typeCode, int tag)
            {
                _pluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
                _typeCode = typeCode;
                _tag = tag;
            }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                var descriptor = new CustomBsonTypeDescriptor(
                    _pluginId,
                    _typeCode,
                    name: $"tag-{_tag}",
                    calculateSize: _ => sizeof(int),
                    serializer: (writer, value) => ((BufferWriter)writer).Write(value.AsInt32),
                    deserializer: reader => new TagValue(_typeCode, ((BufferReader)reader).ReadInt32()),
                    jsonFormatter: value => value.AsInt32.ToString());

                context.RegisterBsonType(descriptor);

                context.Expressions.RegisterFunction(
                    "DB_TAG",
                    new Func<BsonDocument, Collation, BsonDocument, BsonValue, BsonValue>((_, __, ___, ____) => _tag),
                    BsonExpressionType.Call,
                    convertScalarLeftToEnumerable: false,
                    isScalarResult: true);
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
