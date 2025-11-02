using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Vector;

namespace LiteDB.Vector.Tests.Integration
{
    public class VectorIndexLifecycle_Tests
    {
        private sealed class VectorDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        [Fact]
        public void EnsureIndex_BuildsGraph_And_DropCleansMetadata()
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
            var collection = db.GetCollection<VectorDocument>("vectors");

            var documents = new[]
            {
                new VectorDocument { Id = 1, Embedding = new[] { 1f, 0f, 0f } },
                new VectorDocument { Id = 2, Embedding = new[] { 0f, 1f, 0f } },
                new VectorDocument { Id = 3, Embedding = new[] { 0f, 0f, 1f } },
                new VectorDocument { Id = 4, Embedding = new[] { 0.5f, 0.5f, 0f } }
            };

            collection.Insert(documents);

            var created = collection.EnsureIndex(
                "embedding_idx",
                x => x.Embedding,
                new VectorIndexOptions(3, VectorDistanceMetric.Cosine));

            created.Should().BeTrue();

            var state = SnapshotHelper.GetVectorIndexState(db, "vectors", "embedding_idx");

            state.Should().NotBeNull();
            state!.Dimensions.Should().Be((ushort)3);
            state.Metric.Should().Be(VectorDistanceMetric.Cosine);
            state.NodeCount.Should().Be(documents.Length);
            state.Root.IsEmpty.Should().BeFalse();

            var nearest = collection.Query()
                .TopKNear(x => x.Embedding, new[] { 1f, 0f, 0f }, k: 1)
                .First()
                .Id;

            nearest.Should().Be(1);

            collection.Delete(1);

            var afterDelete = SnapshotHelper.GetVectorIndexState(db, "vectors", "embedding_idx");
            afterDelete.Should().NotBeNull();
            afterDelete!.NodeCount.Should().Be(documents.Length - 1);

            var remainingMatch = collection.Query()
                .TopKNear(x => x.Embedding, new[] { 1f, 0f, 0f }, k: 1)
                .First()
                .Id;

            remainingMatch.Should().Be(4);

            var dropped = collection.DropIndex("embedding_idx");
            dropped.Should().BeTrue();

            SnapshotHelper.GetVectorIndexState(db, "vectors", "embedding_idx").Should().BeNull();

            var recreated = collection.EnsureIndex(
                "embedding_idx",
                x => x.Embedding,
                new VectorIndexOptions(3, VectorDistanceMetric.Cosine));

            recreated.Should().BeTrue();

            var recreatedState = SnapshotHelper.GetVectorIndexState(db, "vectors", "embedding_idx");

            recreatedState.Should().NotBeNull();
            recreatedState!.NodeCount.Should().Be(documents.Length);
        }

        private sealed record VectorIndexState(PageAddress Root, ushort Dimensions, VectorDistanceMetric Metric, int NodeCount);

        private static class SnapshotHelper
        {
            private static readonly FieldInfo EngineField = typeof(LiteDatabase).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!;
            private static readonly FieldInfo HeaderField = typeof(LiteEngine).GetField("_header", BindingFlags.NonPublic | BindingFlags.Instance)!;
            private static readonly MethodInfo AutoTransactionDefinition = typeof(LiteEngine)
                .GetMethod("AutoTransaction", BindingFlags.NonPublic | BindingFlags.Instance)!;

            public static VectorIndexState? GetVectorIndexState(LiteDatabase db, string collection, string indexName)
            {
                var engine = (LiteEngine)EngineField.GetValue(db)!;
                var header = (HeaderPage)HeaderField.GetValue(engine)!;
                var collation = header.Pragmas.Collation;

                var method = AutoTransactionDefinition.MakeGenericMethod(typeof(VectorIndexState));

                return (VectorIndexState?)method.Invoke(engine, new object[]
                {
                    new Func<TransactionService, VectorIndexState>(transaction =>
                    {
                        using var snapshot = transaction.CreateSnapshot(LockMode.Read, collection, addIfNotExists: false);
                        var metadata = snapshot.CollectionPage.GetVectorIndexMetadata(indexName);

                        if (metadata == null)
                        {
                            return null!;
                        }

                        var nodeCount = CountNodes(snapshot, metadata.Root);
                        var metric = (VectorDistanceMetric)metadata.Metric;

                        return new VectorIndexState(metadata.Root, metadata.Dimensions, metric, nodeCount);
                    })
                })!;
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

                    var node = snapshot.GetPage<VectorIndexPage>(address.PageID).GetNode(address.Index);
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
        }
    }
}
