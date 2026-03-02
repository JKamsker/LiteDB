using System;
using System.Collections.Concurrent;
using FluentAssertions;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class MissingPluginWarningCacheTests
    {
        [Fact]
        public void Missing_plugin_warning_cache_should_be_scoped_per_database()
        {
            using var firstFile = new TempFile();
            using var secondFile = new TempFile();

            SeedDatabaseWithPluginIndex(firstFile.Filename);
            SeedDatabaseWithPluginIndex(secondFile.Filename);

            var firstLogger = new CollectingLogger();
            using (var firstDatabase = new LiteDatabase(firstFile.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe,
                Logger = firstLogger
            }))
            {
                firstDatabase.GetCollection<TestDocument>("docs").Count().Should().Be(1);
            }

            var secondLogger = new CollectingLogger();
            using (var secondDatabase = new LiteDatabase(secondFile.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe,
                Logger = secondLogger
            }))
            {
                secondDatabase.GetCollection<TestDocument>("docs").Count().Should().Be(1);
            }

            firstLogger.Messages.Should().ContainSingle(x => x.Contains(VectorPlugin.PluginId, StringComparison.Ordinal));
            secondLogger.Messages.Should().ContainSingle(x => x.Contains(VectorPlugin.PluginId, StringComparison.Ordinal));
        }

        private static void SeedDatabaseWithPluginIndex(string filename)
        {
            using var pluginDatabase = new LiteDatabase(filename, plugins: new[] { VectorSearchPlugin.Instance });
            var collection = pluginDatabase.GetCollection<TestDocument>("docs");
            collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
            collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
        }

        private sealed class CollectingLogger : ILogger
        {
            public ConcurrentBag<string> Messages { get; } = new ConcurrentBag<string>();

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                Messages.Add(message ?? string.Empty);
            }
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
