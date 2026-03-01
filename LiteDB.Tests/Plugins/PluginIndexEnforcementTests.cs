using System;
using System.Collections;
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
    public class PluginIndexEnforcementTests
    {
        [Fact]
        public void Enforcement_should_detect_plugin_indexes_even_when_metadata_is_missing()
        {
            using var file = new TempFile();

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                var collection = pluginDatabase.GetCollection<TestDocument>("docs");
                collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } });
                collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();
            }

            using (var pluginDatabase = new LiteDatabase(file.Filename, plugins: new[] { VectorSearchPlugin.Instance }))
            {
                RemovePluginIndexMetadata(pluginDatabase, "docs", "embedding_idx");
            }

            var options = new LiteDatabaseOptions
            {
                MissingPluginBehavior = PluginMissingBehavior.RefuseDatabase
            };

            using var vanillaDatabase = new LiteDatabase(file.Filename, options);
            var vanillaCollection = vanillaDatabase.GetCollection<TestDocument>("docs");

            Action act = () => vanillaCollection.Count();

            act.Should().Throw<LiteException>()
                .Where(ex => ex.ErrorCode == LiteException.PLUGIN_REQUIRED);
        }

        private static void RemovePluginIndexMetadata(LiteDatabase database, string collectionName, string indexName)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (collectionName == null) throw new ArgumentNullException(nameof(collectionName));
            if (indexName == null) throw new ArgumentNullException(nameof(indexName));

            var engineField = typeof(LiteDatabase).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic);
            engineField.Should().NotBeNull();
            var engineValue = engineField.GetValue(database);
            engineValue.Should().BeOfType<LiteEngine>();
            var liteEngine = (LiteEngine)engineValue;

            var monitorField = typeof(LiteEngine).GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic);
            monitorField.Should().NotBeNull();
            var monitorValue = monitorField.GetValue(liteEngine);
            monitorValue.Should().BeOfType<TransactionMonitor>();
            var monitor = (TransactionMonitor)monitorValue;

            var transaction = monitor.GetTransaction(true, queryOnly: false, out _);

            try
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collectionName, addIfNotExists: false);
                var collectionPage = snapshot.CollectionPage;

                var metadataField = typeof(CollectionPage).GetField("_pluginIndexes", BindingFlags.Instance | BindingFlags.NonPublic);
                metadataField.Should().NotBeNull();
                var metadataMap = metadataField.GetValue(collectionPage).Should().BeAssignableTo<IDictionary>().Subject;

                metadataMap.Remove(indexName);
                collectionPage.IsDirty = true;

                transaction.Commit();
            }
            finally
            {
                monitor.ReleaseTransaction(transaction);
            }
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
