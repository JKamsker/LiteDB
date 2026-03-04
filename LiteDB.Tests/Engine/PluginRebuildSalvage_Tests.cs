using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class PluginRebuildSalvage_Tests
    {
        [Fact]
        public void Rebuild_should_require_error_report_when_dropping_orphaned_plugin_indexes()
        {
            using var file = new TempFile();

            using var database = new LiteDatabase(file.Filename);

            Action act = () => database.Rebuild(new RebuildOptions
            {
                DropOrphanedPluginIndexes = true,
                IncludeErrorReport = false
            });

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Rebuild_should_drop_orphaned_plugin_indexes_and_write_error_report()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                pluginDatabase.Checkpoint();
            }

            using (var vanillaDatabase = new LiteDatabase(file.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe
            }))
            {
                vanillaDatabase.Rebuild(new RebuildOptions
                {
                    DropOrphanedPluginIndexes = true,
                    IncludeErrorReport = true
                });
            }

            using var rebuiltDatabase = new LiteDatabase(file.Filename);

            rebuiltDatabase.GetCollection<TestDocument>("docs").Count().Should().Be(1);

            var errors = rebuiltDatabase.GetCollection<BsonDocument>("_rebuild_errors");
            errors.Count().Should().BeGreaterThan(0);
            errors.FindAll().Should().ContainSingle(x => x["message"].AsString.Contains("Dropped orphaned plugin index 'docs.embedding_idx'", StringComparison.Ordinal));
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
