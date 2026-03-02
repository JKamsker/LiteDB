using System;
using System.Linq;
using FluentAssertions;
using LiteDB.Plugins;
using LiteDB.Vector;
using LiteDB.Vector.Utils;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class VectorMetadataCompatibilityTests
    {
        private const string CollectionName = "vectors";
        private const string IndexName = "embedding_idx";

        [Fact]
        public void MissingPlugin_EmitsSingleWarning_AndThrowsLite2002OnVectorOperations()
        {
            using var file = new TempFile();
            SeedVectorDatabase(file.Filename);

            var logger = new CollectingLogger();

            using (var db = new LiteDatabase(file.Filename, new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.AllowIfSafe,
                Logger = logger
            }))
            {
                var collection = db.GetCollection<VectorDocument>(CollectionName);

                collection.Count().Should().Be(3);
                collection.Count().Should().Be(3);

                Action act = () => collection.EnsureIndex(IndexName + "_missing", x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine));

                act.Should().Throw<LiteException>()
                    .Which.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
            }

            logger.Messages.Should().ContainSingle(x => x.Contains(VectorPlugin.PluginId, StringComparison.Ordinal));
        }

        [Fact]
        public void PluginInstalled_AllowsMetadataWithoutWarnings()
        {
            using var file = new TempFile();
            SeedVectorDatabase(file.Filename);

            var logger = new CollectingLogger();

            using (var db = new LiteDatabase(file.Filename, new LiteDatabaseOptions
            {
                Logger = logger,
                Plugins = new[] { VectorSearchPlugin.Instance }
            }))
            {
                var collection = db.GetCollection<VectorDocument>(CollectionName);
                collection.Count().Should().Be(3);

                var recreated = collection.EnsureIndex(IndexName, x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine));
                recreated.Should().BeFalse();
            }

            logger.Messages.Should().NotContain(x => x.Contains("is not loaded", StringComparison.OrdinalIgnoreCase));
        }

        private static void SeedVectorDatabase(string filename)
        {
            using var db = DatabaseFactory.Create(TestDatabaseType.Disk, filename, plugins: new[] { VectorSearchPlugin.Instance });
            var collection = db.GetCollection<VectorDocument>(CollectionName);

            var documents = new[]
            {
                new VectorDocument { Id = 1, Embedding = new[] { 1f, 0.5f, -0.25f, 0.75f, 1.5f, -0.5f, 0.25f, -1f } },
                new VectorDocument { Id = 2, Embedding = new[] { -0.5f, 0.25f, 0.75f, -1.5f, 1f, 0.5f, -0.25f, 0.125f } },
                new VectorDocument { Id = 3, Embedding = new[] { 0.5f, -0.75f, 1.25f, 0.875f, -0.375f, 0.625f, -1.125f, 0.25f } }
            };

            collection.Insert(documents);
            collection.EnsureIndex(IndexName, x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine)).Should().BeTrue();
        }

        private sealed class CollectingLogger : ILogger
        {
            private readonly System.Collections.Generic.List<string> _messages = new System.Collections.Generic.List<string>();

            public System.Collections.Generic.IReadOnlyList<string> Messages => _messages;

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _messages.Add(message);
                }
            }
        }

        private sealed class VectorDocument
        {
            public int Id { get; set; }
            public float[] Embedding { get; set; }
        }
    }
}
