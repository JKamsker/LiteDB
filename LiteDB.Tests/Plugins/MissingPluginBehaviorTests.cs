using System;
using FluentAssertions;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class MissingPluginBehaviorTests
    {
        [Fact]
        public void Missing_plugin_enforcement_should_be_host_controlled()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
            }

            var options = new LiteDatabaseOptions
            {
                Plugins = new ILitePlugin[] { new RelaxedPolicyPlugin() },
                MissingPluginBehavior = PluginMissingBehavior.RefuseDatabase
            };

            using var vanillaDatabase = new LiteDatabase(file.Filename, options);
            var vanillaCollection = vanillaDatabase.GetCollection<TestDocument>("docs");

            Action act = () => vanillaCollection.Count();

            var exception = act.Should().Throw<LiteException>().Which;
            exception.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
            exception.Message.Should().Contain("RelaxedPolicy");
        }

        private sealed class RelaxedPolicyPlugin : ILitePlugin
        {
            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                context.SetDiagnosticPolicy(new RelaxedPolicy());
            }

            private sealed class RelaxedPolicy : IPluginDiagnosticPolicy
            {
                public PluginMissingBehavior MissingBehavior => PluginMissingBehavior.AllowIfSafe;

                public LiteException CreateMissingPluginException(string pluginId, string operation, BsonDocument diagnostics)
                {
                    return new LiteException(LiteException.PLUGIN_REQUIRED, $"RelaxedPolicy: '{pluginId}' is required.");
                }
            }
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}

