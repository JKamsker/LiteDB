using System;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.QueryTest
{
    public class VectorOperatorOptionalityTests
    {
        [Fact]
        public void Parsing_vector_functions_requires_plugin_registration()
        {
            var registry = PluginContextFallbacks.Expressions;

            Action act = () => BsonExpression.Create("VECTOR_DIST($.embedding, $.embedding)", registry);

            var exception = act.Should().Throw<LiteException>().Which;
            exception.Message.Should().Contain("VECTOR_DIST");
        }

        [Fact]
        public void Planner_should_ignore_vector_indexes_without_plugin()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
            }

            using (var vanillaDatabase = new LiteDatabase(file.Filename))
            {
                var collection = vanillaDatabase.GetCollection<TestDocument>("docs");

                Action act = () => collection.Query().Where(x => x.Id == 1).GetPlan();

                var ex = act.Should().Throw<LiteException>().Which;
                ex.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
                ex.Message.Should().Contain(VectorPlugin.PluginId);
            }
        }

        [Fact]
        public void Bson_vector_serialization_should_fail_without_plugin()
        {
            using var database = new LiteDatabase(":memory:");
            var vectors = database.GetCollection<BsonDocument>("vectors");

            var document = new BsonDocument
            {
                ["_id"] = 1,
                ["embedding"] = new BsonVector(new[] { 0.5f, -0.25f, 0.125f })
            };

            Action act = () => vectors.Insert(document);

            var exception = act.Should().Throw<NotSupportedException>().Which;
            exception.Message.Should().Contain("plugin", "error should guide callers to install the plugin");
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
