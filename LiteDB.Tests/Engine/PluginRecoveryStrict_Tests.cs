using System;
using System.IO;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class PluginRecoveryStrict_Tests
    {
        [Fact]
        public void AutoRebuild_should_fail_fast_when_plugin_required()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
                pluginDatabase.Checkpoint();
            }

            MarkDatabaseAsInvalid(file.Filename);

            var settings = new EngineSettings
            {
                Filename = file.Filename,
                AutoRebuild = true
            };

            Action act = () => new LiteEngine(settings);

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        private static void MarkDatabaseAsInvalid(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            stream.Position = HeaderPage.P_INVALID_DATAFILE_STATE;
            stream.WriteByte(1);
            stream.Flush();
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}

