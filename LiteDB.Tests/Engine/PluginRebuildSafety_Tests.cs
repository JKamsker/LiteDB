using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;
using static LiteDB.Constants;

namespace LiteDB.Tests.Engine
{
    public class PluginRebuildSafety_Tests
    {
        [Fact]
        public void Rebuild_should_refuse_when_plugin_indexes_exist_but_plugin_is_missing()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                pluginDatabase.Checkpoint();
            }

            using var vanillaDatabase = new LiteDatabase(file.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe
            });

            Action act = () => vanillaDatabase.Rebuild();

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        [Fact]
        public void FileReaderV8_Open_should_not_swallow_plugin_required()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                pluginDatabase.Checkpoint();
            }

            var logFile = FileHelper.GetLogFile(file.Filename);
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }

            var settings = new EngineSettings { Filename = file.Filename };
            var errors = new List<FileReaderError>();

            using var reader = new FileReaderV8(settings, errors, plugins: null);

            Action act = () => reader.Open();

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
