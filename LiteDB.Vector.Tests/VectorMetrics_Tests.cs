using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Vector;
using LiteDB.Vector.Engine;
using Xunit;

namespace LiteDB.Vector.Tests
{
    public class VectorMetrics_Tests
    {
        private const string IndexName = "embedding_idx";

        private static readonly FieldInfo EngineField = typeof(LiteDatabase).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo HeaderField = typeof(LiteEngine).GetField("_header", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo AutoTransactionMethod = typeof(LiteEngine).GetMethod("AutoTransaction", BindingFlags.NonPublic | BindingFlags.Instance)!;

        [Fact]
        public void ComputeDistance_RespectsMetricRanges()
        {
            var target = new[] { 1f, 0f, 0f };
            var identical = new[] { 1f, 0f, 0f };
            var opposite = new[] { -1f, 0f, 0f };
            var diagonal = new[] { 0.5f, 0.5f, 0f };

            var cosineIdentical = VectorIndexService.ComputeDistance(identical, target, VectorDistanceMetric.Cosine, out _);
            var cosineOpposite = VectorIndexService.ComputeDistance(opposite, target, VectorDistanceMetric.Cosine, out _);
            var cosineDiagonal = VectorIndexService.ComputeDistance(diagonal, target, VectorDistanceMetric.Cosine, out _);

            cosineIdentical.Should().BeApproximately(0d, 1e-6);
            cosineOpposite.Should().BeApproximately(2d, 1e-6);
            cosineDiagonal.Should().BeGreaterOrEqualTo(0d).And.BeLessOrEqualTo(2d);

            var euclideanIdentical = VectorIndexService.ComputeDistance(identical, target, VectorDistanceMetric.Euclidean, out _);
            var euclideanOpposite = VectorIndexService.ComputeDistance(opposite, target, VectorDistanceMetric.Euclidean, out _);
            var euclideanDiagonal = VectorIndexService.ComputeDistance(diagonal, target, VectorDistanceMetric.Euclidean, out _);

            euclideanIdentical.Should().BeApproximately(0d, 1e-6);
            euclideanOpposite.Should().BeGreaterThan(0d);
            euclideanDiagonal.Should().BeGreaterOrEqualTo(0d);

            var dotIdentical = VectorIndexService.ComputeDistance(identical, target, VectorDistanceMetric.DotProduct, out var dotIdenticalSimilarity);
            var dotOpposite = VectorIndexService.ComputeDistance(opposite, target, VectorDistanceMetric.DotProduct, out var dotOppositeSimilarity);

            dotIdenticalSimilarity.Should().BeApproximately(1d, 1e-6);
            dotIdentical.Should().BeApproximately(-1d, 1e-6);

            dotOppositeSimilarity.Should().BeApproximately(-1d, 1e-6);
            dotOpposite.Should().BeApproximately(1d, 1e-6);
        }

        [Fact]
        public void CrossMetricRanking_ProducesDifferentOrderings()
        {
            var target = new[] { 1f, 0f };

            var candidates = new Dictionary<string, float[]>
            {
                ["A"] = new[] { 2f, 0f },
                ["B"] = new[] { 0.8f, 0.6f },
                ["C"] = new[] { 0f, 1f }
            };

            var cosineRanking = RankCandidates(candidates, target, VectorDistanceMetric.Cosine);
            var euclideanRanking = RankCandidates(candidates, target, VectorDistanceMetric.Euclidean);
            var dotRanking = RankCandidates(candidates, target, VectorDistanceMetric.DotProduct);

            cosineRanking.Should().Equal(new[] { "A", "B", "C" });
            dotRanking.Should().Equal(new[] { "A", "B", "C" });
            euclideanRanking.Should().Equal(new[] { "B", "A", "C" });

            euclideanRanking.Should().NotEqual(cosineRanking);
            euclideanRanking.Should().NotEqual(dotRanking);
        }

        [Theory]
        [InlineData(VectorDistanceMetric.Cosine)]
        [InlineData(VectorDistanceMetric.Euclidean)]
        [InlineData(VectorDistanceMetric.DotProduct)]
        public void EnsureIndex_UsesSpecifiedMetric(VectorDistanceMetric metric)
        {
            var file = CreateTempDatabasePath();

            try
            {
                using var db = new LiteDatabase(file, plugins: new[] { VectorSearchPlugin.Instance });
                var collection = db.GetCollection<TestDocument>("docs");

                var documents = new[]
                {
                    new TestDocument { Id = 1, Embedding = new[] { 1f, 0f } },
                    new TestDocument { Id = 2, Embedding = new[] { 0.8f, 0.6f } },
                    new TestDocument { Id = 3, Embedding = new[] { -0.4f, 0.9f } }
                };

                collection.Insert(documents);

                var created = collection.EnsureIndex(
                    IndexName,
                    CreateExpression(db, "$.Embedding"),
                    new VectorIndexOptions(2, metric));

                created.Should().BeTrue();

                var target = new[] { 1f, 0f };

                var metadataMetric = InspectVectorIndex(db, "docs", (_, _, metadata) =>
                {
                    metadata.Should().NotBeNull();
                    return (VectorDistanceMetric)metadata.Metric;
                });

                metadataMetric.Should().Be(metric);

                var actualResults = InspectVectorIndex(db, "docs", (snapshot, collation, metadata) =>
                {
                    var service = new VectorIndexService(snapshot, collation);

                    return service.Search(metadata, target, double.MaxValue, documents.Length)
                        .Select(result => (Document: BsonMapper.Global.ToObject<TestDocument>(result.Document), Score: result.Distance))
                        .ToList();
                });

                var expected = documents
                    .Select(doc =>
                    {
                        var distance = VectorIndexService.ComputeDistance(doc.Embedding, target, metric, out var similarity);
                        var score = metric == VectorDistanceMetric.DotProduct ? similarity : distance;
                        return (doc.Id, Score: score);
                    })
                    .ToList();

                var expectedOrdered = metric == VectorDistanceMetric.DotProduct
                    ? expected.OrderByDescending(x => x.Score).ToList()
                    : expected.OrderBy(x => x.Score).ToList();

                var actualOrdered = metric == VectorDistanceMetric.DotProduct
                    ? actualResults.OrderByDescending(x => x.Score).ToList()
                    : actualResults.OrderBy(x => x.Score).ToList();

                actualOrdered.Select(x => x.Document.Id).Should().Equal(expectedOrdered.Select(x => x.Id));

                for (var i = 0; i < expectedOrdered.Count; i++)
                {
                    actualOrdered[i].Score.Should().BeApproximately(expectedOrdered[i].Score, 1e-6);
                }
            }
            finally
            {
                DeleteIfExists(file);
            }
        }

        private static BsonExpression CreateExpression(LiteDatabase db, string expression)
        {
            return BsonExpression.Create(expression, db.Services.ExpressionRegistry);
        }

        private static T InspectVectorIndex<T>(LiteDatabase db, string collection, Func<Snapshot, Collation, VectorIndexMetadata, T> selector)
        {
            var engine = (LiteEngine)EngineField.GetValue(db)!;
            var header = (HeaderPage)HeaderField.GetValue(engine)!;
            var collation = header.Pragmas.Collation;
            var method = AutoTransactionMethod.MakeGenericMethod(typeof(T));

            return (T)method.Invoke(engine, new object[]
            {
                new Func<TransactionService, T>(transaction =>
                {
                    using var snapshot = transaction.CreateSnapshot(LockMode.Read, collection, false);
                    var metadataBuffer = snapshot.CollectionPage.GetVectorIndexMetadata(IndexName);
                    if (metadataBuffer == null)
                    {
                        throw new InvalidOperationException($"Vector index '{IndexName}' was not found in collection '{collection}'.");
                    }

                    var metadata = VectorIndexMetadata.Wrap(metadataBuffer);

                    return selector(snapshot, collation, metadata);
                })
            })!;
        }

        private static IReadOnlyList<string> RankCandidates(Dictionary<string, float[]> candidates, float[] target, VectorDistanceMetric metric)
        {
            var scored = candidates.Select(pair =>
            {
                var distance = VectorIndexService.ComputeDistance(pair.Value, target, metric, out var similarity);
                var score = metric == VectorDistanceMetric.DotProduct ? similarity : distance;
                return (pair.Key, Score: score);
            });

            return metric == VectorDistanceMetric.DotProduct
                ? scored.OrderByDescending(x => x.Score).Select(x => x.Key).ToArray()
                : scored.OrderBy(x => x.Score).Select(x => x.Key).ToArray();
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        private static string CreateTempDatabasePath()
        {
            return Path.Combine(Path.GetTempPath(), $"vector-metrics-{Guid.NewGuid():N}.db");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
