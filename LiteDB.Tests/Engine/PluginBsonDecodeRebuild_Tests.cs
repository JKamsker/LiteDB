using System;
using FluentAssertions;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class PluginBsonDecodeRebuild_Tests
    {
        [Fact]
        public void Rebuild_should_refuse_when_documents_require_plugin_bson_types()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<BsonDocument>("docs");

                collection.Insert(new BsonDocument
                {
                    ["_id"] = 1,
                    ["vec"] = new BsonVector(new[] { 0.1f, 0.2f, 0.3f })
                });

                pluginDatabase.Checkpoint();
            }

            using (var vanillaDatabase = new LiteDatabase(file.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe
            }))
            {
                Action act = () => vanillaDatabase.Rebuild();

                act.Should().Throw<LiteException>()
                    .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
            }

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var doc = pluginDatabase.GetCollection<BsonDocument>("docs").FindById(1);

                ((object)doc).Should().NotBeNull();
                ((object)doc["vec"]).Should().BeOfType<BsonVector>();
                ((BsonVector)doc["vec"]).Values.Should().Equal(new[] { 0.1f, 0.2f, 0.3f });
            }
        }
    }
}
