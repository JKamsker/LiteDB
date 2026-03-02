using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
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

        [Fact]
        public void SysPlugins_should_not_duplicate_collection_scan_errors_for_multiple_plugin_indexes()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx1", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                collection.EnsureIndex("embedding_idx2", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
            }

            using var engine = new LiteEngine(new EngineSettings { Filename = file.Filename });

            var injected = 0;

            engine.SimulateDiskReadFail = buffer =>
            {
                if (buffer == null)
                {
                    return;
                }

                if (buffer.ReadByte(BasePage.P_PAGE_TYPE) != (byte)PageType.Collection)
                {
                    return;
                }

                if (Interlocked.Exchange(ref injected, 1) != 0)
                {
                    return;
                }

                var area = buffer.Slice(Constants.PAGE_HEADER_SIZE, Constants.PAGE_SIZE - Constants.PAGE_HEADER_SIZE);

                using (var reader = new BufferReader(new[] { area }, false))
                {
                    for (var i = 0; i < Constants.PAGE_FREE_LIST_SLOTS; i++)
                    {
                        reader.ReadUInt32();
                    }

                    reader.Skip(CollectionPage.P_INDEXES - Constants.PAGE_HEADER_SIZE - reader.Position);

                    var indexCount = reader.ReadByte();

                    for (var i = 0; i < indexCount; i++)
                    {
                        reader.ReadByte(); // slot
                        reader.ReadByte(); // indexType
                        reader.ReadCString(); // name

                        reader.ReadCString(); // expression
                        reader.ReadBoolean(); // unique
                        reader.ReadPageAddress(); // head
                        reader.ReadPageAddress(); // tail
                        reader.ReadByte(); // reserved
                        reader.ReadUInt32(); // free index page list
                    }

                    if (reader.IsEOF)
                    {
                        return;
                    }

                    var metadataCount = reader.ReadByte();

                    if (metadataCount == 0)
                    {
                        return;
                    }

                    reader.ReadCString(); // indexName

                    var markerOffset = Constants.PAGE_HEADER_SIZE + reader.Position;

                    // Clear the marker bit to force TryReadPluginMetadataEntry() into the legacy-format error path.
                    buffer[markerOffset] = (byte)(buffer[markerOffset] & 0x7F);
                }
            };

            using var vanillaDatabase = new LiteDatabase(engine, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe
            });

            var plugins = vanillaDatabase.GetCollection("$plugins")
                .FindAll()
                .ToArray();

            var row = plugins.Single(x => x["indexCount"].AsInt32 == 2);

            row["errors"].AsArray.Count.Should().Be(1);
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
