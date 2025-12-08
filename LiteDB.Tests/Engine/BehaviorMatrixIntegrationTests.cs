using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public class BehaviorMatrixIntegrationTests
    {
        private const string CollectionName = "vectors";
        private const string IndexName = "embedding_idx";

        [Fact]
        public void PrereleaseArtifactsSafe_WithoutPlugin_WarnsOnceAndBlocksVectorOps()
        {
            RunScenario(pluginPresent: false, safeToIgnore: true);
        }

        [Fact]
        public void PrereleaseArtifactsUnsafe_WithoutPlugin_RefusesDatabase()
        {
            Action action = () => RunScenario(pluginPresent: false, safeToIgnore: false);
            action.Should().Throw<LiteException>().Which.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
        }

        [Fact]
        public void GaMetadataWithoutPlugin_WarnsOnceAndBlocksVectorOps()
        {
            RunScenario(pluginPresent: false, safeToIgnore: null);
        }

        [Fact]
        public void GaMetadataWithPlugin_AllowsFullAccess()
        {
            RunScenario(pluginPresent: true, safeToIgnore: null);
        }

        [Fact]
        public void ShrinkFailsWhenPluginMissing()
        {
            using var file = new TempFile();
            SeedDatabase(file.Filename);
            ClearPluginWarningCache();

            using var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename);
            Action action = () => db.Rebuild();
            action.Should().Throw<LiteException>().Which.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
        }

        [Fact]
        public void ShrinkSucceedsWithPlugin()
        {
            using var file = new TempFile();
            SeedDatabase(file.Filename);

            using var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename, plugins: new[] { VectorSearchPlugin.Instance });
            db.Rebuild();
        }

        private static void RunScenario(bool pluginPresent, bool? safeToIgnore)
        {
            using var file = new TempFile();
            SeedDatabase(file.Filename, safeToIgnore);
            ClearPluginWarningCache();

            var plugins = pluginPresent ? new[] { VectorSearchPlugin.Instance } : null;

            Action scenario = () =>
            {
                using var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename, plugins: plugins);
                var collection = db.GetCollection<VectorDocument>(CollectionName);
                collection.Count().Should().Be(3);

                if (pluginPresent)
                {
                    collection.EnsureIndex(IndexName, x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine)).Should().BeFalse();
                }
                else
                {
                    var ex = Assert.Throws<LiteException>(() =>
                        collection.EnsureIndex(IndexName + "_missing", x => x.Embedding, new VectorIndexOptions(8, VectorDistanceMetric.Cosine)));
                    ex.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
                }
            };

            if (safeToIgnore == false && !pluginPresent)
            {
                scenario.Should().Throw<LiteException>().Which.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
            }
            else
            {
                scenario();
                var cache = GetPluginWarningCache();
                if (pluginPresent)
                {
                    cache.Keys.Should().BeEmpty();
                }
                else if (safeToIgnore == true || safeToIgnore is null)
                {
                    cache.Keys.Should().ContainSingle(key => key == VectorPlugin.PluginId);
                }
            }
        }

        private static void SeedDatabase(string filename, bool? safeToIgnore = true)
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

            ClearPluginWarningCache();
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
