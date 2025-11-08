using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
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
            var diagnostics = exception.Data["VectorDiagnostics"].Should().BeOfType<BsonDocument>().Which;
            diagnostics["event"].AsString.Should().Be("vector.plugin_required");
            diagnostics["operation"].AsString.Should().Be("EnsureVectorIndex");
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

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
