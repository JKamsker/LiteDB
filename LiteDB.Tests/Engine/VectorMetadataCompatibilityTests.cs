using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB.Plugins;
using LiteDB.Vector;
using LiteDB.Vector.Utils;
using LiteDB.Tests.Utils;
using LiteDB.Engine;
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
            ClearPluginWarningCache();

            using (var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename))
            {
                var collection = db.GetCollection<VectorDocument>(CollectionName);
                collection.Count().Should().Be(3);

                var ex = Assert.Throws<LiteException>(() =>
                    collection.EnsureIndex("missing_plugin_idx", x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine)));

                ex.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
                ex.Message.Should().Contain(VectorPlugin.PluginId);
            }

            var cache = GetPluginWarningCache();
            cache.Keys.Should().ContainSingle(key => key == VectorPlugin.PluginId);

            using (var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename))
            {
                db.GetCollection<VectorDocument>(CollectionName).Count().Should().Be(3);
            }

            cache.Keys.Should().ContainSingle(key => key == VectorPlugin.PluginId);
        }

        [Fact]
        public void PluginInstalled_AllowsMetadataWithoutWarnings()
        {
            using var file = new TempFile();
            SeedVectorDatabase(file.Filename);
            ClearPluginWarningCache();

            using (var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = db.GetCollection<VectorDocument>(CollectionName);
                collection.Count().Should().Be(3);

                var recreated = collection.EnsureIndex(IndexName, x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine));
                recreated.Should().BeFalse();
            }

            GetPluginWarningCache().Keys.Should().BeEmpty();
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

        private static void ClearPluginWarningCache() => GetPluginWarningCache().Clear();

        private static ConcurrentDictionary<string, byte> GetPluginWarningCache()
        {
            var field = typeof(Snapshot).GetField("_missingPluginWarnings", BindingFlags.NonPublic | BindingFlags.Static);
            return (ConcurrentDictionary<string, byte>)field.GetValue(null);
        }

        private sealed class VectorDocument
        {
            public int Id { get; set; }
            public float[] Embedding { get; set; }
        }
    }
}
