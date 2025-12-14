using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using LiteDB.Vector;
using LiteDB.Vector.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class PageFactoryRegistry_Tests
    {
        private const string CollectionName = "vectors";
        private const string VectorIndexName = "embedding_idx";

        private static readonly FieldInfo EngineField = typeof(LiteDatabase).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo AutoTransactionMethod = typeof(LiteEngine).GetMethod("AutoTransaction", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo PluginContextField = typeof(LiteDatabase).GetField("_pluginContext", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly Type PageFactoryResolverType = typeof(LiteDatabase).Assembly.GetType("LiteDB.Engine.PageFactoryResolver")!;
        private static readonly MethodInfo GetPageFactoryRegistryMethod = PageFactoryResolverType.GetMethod("GetRegistry", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        private static readonly Type PageFactoryRegistryType = typeof(LiteDatabase).Assembly.GetType("LiteDB.Engine.PageFactoryRegistry")!;
        private static readonly MethodInfo TryGetPageFactoryRegistrationMethod = PageFactoryRegistryType.GetMethod("TryGetRegistration", BindingFlags.Instance | BindingFlags.Public)!;
        private static readonly Type PageTypeEnum = typeof(LiteDatabase).Assembly.GetType("LiteDB.Engine.PageType")!;
        private static readonly object VectorIndexPageType = Enum.ToObject(PageTypeEnum, VectorPlugin.PageTypeCode);

        private sealed class VectorDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        [Fact]
        public void VectorPagesWithoutPluginShouldReportPluginRequirement()
        {
            using var file = new TempFile();

            SeedVectorIndex(file.Filename);

            Action act = () =>
            {
                using var db = DatabaseFactory.Create(TestDatabaseType.Disk, file.Filename);

                InspectVectorIndex(db, CollectionName, VectorIndexName, (snapshot, metadata) =>
                {
                    CountNodes(snapshot, metadata.Root);
                    return 0;
                });
            };

            act.Should()
                .Throw<LiteException>()
                .Which.ErrorCode.Should().Be(LiteException.PLUGIN_REQUIRED);
        }

        [Fact]
        public void PageFactoryRegistriesShouldStayIsolatedAcrossPluginContexts()
        {
            using var databases = DatabaseFactory.CreateMany(
                DatabaseFactoryOptions.InMemory(plugins: new[] { VectorSearchPlugin.Instance }),
                DatabaseFactoryOptions.InMemory());

            var vectorRegistry = GetPageFactoryRegistry(databases[0]);
            var baselineRegistry = GetPageFactoryRegistry(databases[1]);

            HasVectorPageRegistration(vectorRegistry).Should().BeTrue("the vector plugin registers the VectorIndex page when installed");
            HasVectorPageRegistration(baselineRegistry).Should().BeFalse("vector page registrations must not leak into plugin-free contexts");
        }

        private static void SeedVectorIndex(string filename)
        {
            using var db = DatabaseFactory.Create(
                TestDatabaseType.Disk,
                filename,
                plugins: new[] { VectorSearchPlugin.Instance });

            var collection = db.GetCollection<VectorDocument>(CollectionName);
            var documents = Enumerable.Range(1, 6)
                .Select(i => new VectorDocument
                {
                    Id = i,
                    Embedding = CreateVector(i, 5)
                })
                .ToList();

            collection.Insert(documents);

            var options = new VectorIndexOptions(dimensions: 5, VectorDistanceMetric.Cosine);
            collection.EnsureIndex(VectorIndexName, x => x.Embedding, options).Should().BeTrue();

            db.Checkpoint();
        }

        private static T InspectVectorIndex<T>(
            LiteDatabase db,
            string collection,
            string indexName,
            Func<Snapshot, VectorIndexMetadata, T> selector)
        {
            return ExecuteInTransaction(db, transaction =>
            {
                using var snapshot = transaction.CreateSnapshot(LockMode.Read, collection, addIfNotExists: false);
                var metadataBuffer = snapshot.CollectionPage.GetPluginIndexMetadata(indexName)
                    ?? throw new InvalidOperationException($"Vector index '{indexName}' metadata not found.");

                var metadata = VectorIndexMetadata.Wrap(metadataBuffer);
                return selector(snapshot, metadata);
            });
        }

        private static int CountNodes(Snapshot snapshot, PageAddress root)
        {
            if (root.IsEmpty)
            {
                return 0;
            }

            var visited = new HashSet<PageAddress>();
            var queue = new Queue<PageAddress>();
            queue.Enqueue(root);

            var count = 0;

            while (queue.Count > 0)
            {
                var address = queue.Dequeue();
                if (!visited.Add(address))
                {
                    continue;
                }

                var page = snapshot.GetPage<VectorIndexPage>(address.PageID);
                var node = page.GetNode(address.Index);
                count++;

                for (var level = 0; level < node.LevelCount; level++)
                {
                    foreach (var neighbor in node.GetNeighbors(level))
                    {
                        if (!neighbor.IsEmpty)
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return count;
        }

        private static T ExecuteInTransaction<T>(LiteDatabase db, Func<TransactionService, T> action)
        {
            var engine = (LiteEngine)EngineField.GetValue(db)!;
            var method = AutoTransactionMethod.MakeGenericMethod(typeof(T));
            try
            {
                return (T)method.Invoke(engine, new object[] { action })!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // Unreachable, but required by compiler.
            }
        }

        private static float[] CreateVector(int seed, int dimensions)
        {
            return Enumerable.Range(0, dimensions)
                .Select(i => (float)Math.Sin((seed * 0.37) + (i * 0.11)))
                .ToArray();
        }

        private static object GetPageFactoryRegistry(LiteDatabase database)
        {
            var context = PluginContextField.GetValue(database) ?? throw new InvalidOperationException("Plugin context not found.");
            return GetPageFactoryRegistryMethod.Invoke(null, new[] { context }) ?? throw new InvalidOperationException("Registry resolution failed.");
        }

        private static bool HasVectorPageRegistration(object registry)
        {
            var args = new object[] { VectorIndexPageType, null };
            return (bool)TryGetPageFactoryRegistrationMethod.Invoke(registry, args);
        }
    }
}
