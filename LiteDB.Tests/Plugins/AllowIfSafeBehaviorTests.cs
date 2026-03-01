using System;
using FluentAssertions;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class AllowIfSafeBehaviorTests
    {
        [Fact]
        public void AllowIfSafe_should_allow_reads_but_refuse_writes_on_affected_collections()
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
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe
            };

            using var vanillaDatabase = new LiteDatabase(file.Filename, options);

            var affected = vanillaDatabase.GetCollection<TestDocument>("docs");
            affected.Count().Should().Be(1);

            Action write = () => affected.Insert(new TestDocument { Id = 2, Embedding = new[] { 0.5f, 0.6f, 0.7f } });
            write.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);

            var clean = vanillaDatabase.GetCollection<CleanDocument>("clean");
            clean.Insert(new CleanDocument { Id = 1, Name = "alpha" });
            clean.Count().Should().Be(1);
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }

        private sealed class CleanDocument
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }
    }
}

