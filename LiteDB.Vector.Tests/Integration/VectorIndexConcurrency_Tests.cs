using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using LiteDB.Vector;

namespace LiteDB.Vector.Tests.Integration
{
    public class VectorIndexConcurrency_Tests
    {
        private sealed class VectorDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        [Fact]
        public async Task VectorQueriesMaintainIsolationDuringConcurrentUpdates()
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
            var collection = db.GetCollection<VectorDocument>("vectors");

            var moving = new VectorDocument { Id = 1, Embedding = new[] { 1f, 0f } };
            var stable = new VectorDocument { Id = 2, Embedding = new[] { 1f, 0f } };

            collection.Insert(new[] { moving, stable });

            collection.EnsureIndex(
                "embedding_idx",
                x => x.Embedding,
                new VectorIndexOptions(2, VectorDistanceMetric.Cosine));

            var target = new[] { 1f, 0f };

            var observed = new ConcurrentBag<int>();
            var errors = new ConcurrentQueue<Exception>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var queryTask = Task.Run(() =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        var result = collection.Query()
                            .TopKNear(x => x.Embedding, target, k: 1)
                            .FirstOrDefault();

                        result.Should().NotBeNull();
                        (result!.Id == moving.Id || result.Id == stable.Id).Should().BeTrue();
                        observed.Add(result.Id);
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                        cancellation.Cancel();
                    }
                }
            }, cancellation.Token);

            var updateTask = Task.Run(() =>
            {
                for (var i = 0; i < 200 && !cancellation.IsCancellationRequested; i++)
                {
                    var vector = (i % 2 == 0) ? new[] { 1f, 0f } : new[] { 0f, 1f };

                    if (!db.BeginTrans())
                    {
                        continue;
                    }

                    try
                    {
                        collection.Update(new VectorDocument { Id = moving.Id, Embedding = vector });
                        db.Commit();
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                        db.Rollback();
                        cancellation.Cancel();
                        break;
                    }
                }

                cancellation.Cancel();
            }, cancellation.Token);

            await Task.WhenAll(queryTask, updateTask);

            errors.Should().BeEmpty();
            observed.Should().NotBeEmpty();
            observed.Should().OnlyContain(id => id == moving.Id || id == stable.Id);
            observed.Should().Contain(moving.Id);
            observed.Should().Contain(stable.Id);
        }
    }
}
