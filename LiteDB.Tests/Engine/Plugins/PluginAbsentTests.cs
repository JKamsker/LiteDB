using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Engine.Plugins
{
    public class PluginAbsentTests
    {
        private static readonly string MissingPluginMessage = VectorCompatibility.PluginRequired().Message;

        [Fact]
        public void EnsureVectorIndex_WithoutPlugin_ThrowsDeterministicError()
        {
            using var db = new LiteDatabase(":memory:");
            var collection = db.GetCollection<TestDocument>("docs");

            Action act = () => collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3));

            var exception = act.Should().Throw<LiteException>().Which;
            exception.Message.Should().Be(MissingPluginMessage);

            var hasDiagnostics = exception.Data.Contains("VectorDiagnostics");
            hasDiagnostics.Should().BeTrue("vector diagnostics should be emitted when the plugin is absent");

            var diagnosticsPayload = exception.Data["VectorDiagnostics"];
            diagnosticsPayload.Should().NotBeNull();

            var diagnostics = diagnosticsPayload as BsonDocument
                ?? JsonSerializer.Deserialize(diagnosticsPayload.ToString()).AsDocument;

            diagnostics["event"].AsString.Should().Be("plugin.index_required");
            diagnostics["operation"].AsString.Should().Be("EnsureCustomIndex");
            diagnostics["strategyKind"].AsString.Should().Be(VectorCompatibility.DefaultStrategyKind);
            diagnostics["collection"].AsString.Should().Be("docs");
            diagnostics["pluginContextAvailable"].AsBoolean.Should().BeTrue();
            diagnostics["registeredStrategies"].AsArray.RawValue.Should().BeEmpty();
        }

        [Fact]
        public void VectorQueryOperators_WithoutPlugin_ThrowDeterministicError()
        {
            using var db = new LiteDatabase(":memory:");
            var collection = db.GetCollection<TestDocument>("docs");

            Action act = () =>
            {
                collection
                    .Query()
                    .WhereNear(x => x.Embedding, new[] { 0.1f, 0.2f, 0.3f }, maxDistance: 0.5);
            };

            var exception = act.Should().Throw<LiteException>().Which;
            exception.Message.Should().Be(MissingPluginMessage);
        }

        [Fact]
        public void VectorBsonType_ShouldRemainScopedToPluginEnabledDatabases()
        {
            using var databases = DatabaseFactory.CreateMany(
                DatabaseFactoryOptions.InMemory(plugins: new[] { VectorSearchPlugin.Instance }),
                DatabaseFactoryOptions.InMemory());

            var pluginVectors = databases[0].GetCollection<BsonDocument>("vectors");
            var vectorDocument = CreateVectorDocument(1);
            pluginVectors.Insert(vectorDocument);

            var reloaded = pluginVectors.FindById(1);
            reloaded["embedding"].AsVector.Should().Equal(vectorDocument["embedding"].AsVector);

            var vanillaCollection = databases[1].GetCollection<TestDocument>("vectors");
            vanillaCollection.Insert(new TestDocument { Id = 1, Embedding = new[] { 1f, 0f, 0f } });

            Action act = () =>
            {
                vanillaCollection
                    .Query()
                    .WhereNear(x => x.Embedding, new[] { 1f, 0f, 0f }, maxDistance: 0.1)
                    .ToList();
            };

            act.Should().Throw<LiteException>().Which.Message.Should().Be(MissingPluginMessage);
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        private static BsonDocument CreateVectorDocument(int id)
        {
            return new BsonDocument
            {
                ["_id"] = id,
                ["embedding"] = new BsonVector(new[] { 0.1f * id, 0.2f * id, 0.3f * id })
            };
        }
    }
}
