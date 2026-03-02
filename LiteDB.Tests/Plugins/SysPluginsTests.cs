using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class SysPluginsTests
    {
        [Fact]
        public void SysPlugins_should_work_under_strict_mode_without_poisoning_warning_cache()
        {
            ClearMissingPluginWarnings();

            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
            }

            var logger = new CollectingLogger();
            var options = new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.RefuseDatabase,
                Logger = logger
            };

            using var vanillaDatabase = new LiteDatabase(file.Filename, options);
            var plugins = vanillaDatabase.GetCollection("$plugins")
                .FindAll()
                .ToArray();

            GetMissingPluginWarningCount().Should().Be(0);
            logger.Messages.Should().BeEmpty();

            plugins.Should().ContainSingle(row => row["key"].AsString == VectorPlugin.PluginId);

            var vectorRow = plugins.Single(row => row["key"].AsString == VectorPlugin.PluginId);
            vectorRow["pluginId"].AsString.Should().Be(VectorPlugin.PluginId);
            vectorRow["loaded"].AsBoolean.Should().BeFalse();
            vectorRow["strategyAvailable"].AsBoolean.Should().BeFalse();
            vectorRow["indexCount"].AsInt32.Should().Be(1);
            vectorRow["collections"].AsArray.Select(x => x.AsString).Should().Contain("docs");
            vectorRow["errors"].AsArray.Count.Should().Be(0);
        }

        private static void ClearMissingPluginWarnings()
        {
            var field = typeof(Snapshot).GetField("_missingPluginWarnings", BindingFlags.NonPublic | BindingFlags.Static);
            field.Should().NotBeNull();

            var cache = field.GetValue(null);
            cache.Should().NotBeNull();

            var clear = cache.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            clear.Should().NotBeNull();

            clear.Invoke(cache, null);
        }

        private static int GetMissingPluginWarningCount()
        {
            var field = typeof(Snapshot).GetField("_missingPluginWarnings", BindingFlags.NonPublic | BindingFlags.Static);
            field.Should().NotBeNull();

            var cache = field.GetValue(null);
            cache.Should().NotBeNull();

            var count = cache.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            count.Should().NotBeNull();

            return (int)count.GetValue(cache);
        }

        private sealed class CollectingLogger : ILogger
        {
            private readonly List<string> _messages = new List<string>();

            public IReadOnlyList<string> Messages => _messages;

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                _messages.Add(message ?? string.Empty);
            }
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
