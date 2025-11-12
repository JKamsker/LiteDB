using System;
using System.Collections.Generic;
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
    public class VectorExtensions_Tests
    {
        private class VectorDocument
        {
            public int Id { get; set; }
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        private static readonly FieldInfo EngineField = typeof(LiteDatabase).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unable to resolve LiteDatabase._engine field.");

        private static readonly MethodInfo AutoTransactionMethod = typeof(LiteEngine).GetMethod("AutoTransaction", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unable to resolve LiteEngine.AutoTransaction method.");

        [Fact]
        public void CollectionExtensions_CreateVectorIndexesForAllOverloads()
        {
            using var db = CreateDatabase();

            var namedOptions = new VectorIndexOptions(dimensions: 3, metric: VectorDistanceMetric.DotProduct);
            var namedCollection = db.GetCollection<VectorDocument>("named_vectors");
            namedCollection.EnsureIndex("embedding_idx", "$.Embedding", namedOptions).Should().BeTrue();
            var namedMetadata = GetVectorMetadata(db, "named_vectors", "embedding_idx");
            namedMetadata.Should().NotBeNull();
            namedMetadata!.Dimensions.Should().Be((ushort)namedOptions.Dimensions);
            namedMetadata.Metric.Should().Be((byte)namedOptions.Metric);

            var expressionOptions = new VectorIndexOptions(dimensions: 2, metric: VectorDistanceMetric.Euclidean);
            var expressionCollection = db.GetCollection<VectorDocument>("expr_vectors");
            expressionCollection.EnsureIndex("$.Embedding", expressionOptions).Should().BeTrue();
            var expressionMetadata = GetVectorMetadata(db, "expr_vectors", "Embedding");
            expressionMetadata.Should().NotBeNull();
            expressionMetadata!.Metric.Should().Be((byte)expressionOptions.Metric);

            var lambdaCollection = db.GetCollection<VectorDocument>("lambda_vectors");
            var lambdaOptions = new VectorIndexOptions(dimensions: 4);
            lambdaCollection.EnsureIndex(x => x.Embedding, lambdaOptions).Should().BeTrue();
            var lambdaMetadata = GetVectorMetadata(db, "lambda_vectors", "Embedding");
            lambdaMetadata.Should().NotBeNull();
            lambdaMetadata!.Dimensions.Should().Be((ushort)lambdaOptions.Dimensions);
            lambdaMetadata.Metric.Should().Be((byte)lambdaOptions.Metric);

            var lambdaNamedCollection = db.GetCollection<VectorDocument>("lambda_named_vectors");
            var lambdaNamedOptions = new VectorIndexOptions(dimensions: 5, metric: VectorDistanceMetric.DotProduct);
            lambdaNamedCollection.EnsureIndex("lambda_named_idx", x => x.Embedding, lambdaNamedOptions).Should().BeTrue();
            var lambdaNamedMetadata = GetVectorMetadata(db, "lambda_named_vectors", "lambda_named_idx");
            lambdaNamedMetadata.Should().NotBeNull();
            lambdaNamedMetadata!.Dimensions.Should().Be((ushort)lambdaNamedOptions.Dimensions);
            lambdaNamedMetadata.Metric.Should().Be((byte)lambdaNamedOptions.Metric);
        }

        [Fact]
        public void RepositoryExtensions_CreateVectorIndexesAcrossCollections()
        {
            using var db = CreateDatabase();
            var repository = new LiteRepository(db);

            var defaultOptions = new VectorIndexOptions(3, VectorDistanceMetric.Cosine);
            repository.EnsureIndex<VectorDocument>("embedding_idx", "$.Embedding", defaultOptions).Should().BeTrue();
            var defaultMetadata = GetVectorMetadata(db, nameof(VectorDocument), "embedding_idx");
            defaultMetadata.Should().NotBeNull();
            defaultMetadata!.Metric.Should().Be((byte)defaultOptions.Metric);

            var expressionOptions = new VectorIndexOptions(2, VectorDistanceMetric.Euclidean);
            repository.EnsureIndex<VectorDocument>("$.Embedding", expressionOptions, collectionName: "custom_vectors").Should().BeTrue();
            var expressionMetadata = GetVectorMetadata(db, "custom_vectors", "Embedding");
            expressionMetadata.Should().NotBeNull();
            expressionMetadata!.Dimensions.Should().Be((ushort)expressionOptions.Dimensions);

            var lambdaOptions = new VectorIndexOptions(2, VectorDistanceMetric.DotProduct);
            repository.EnsureIndex<VectorDocument, float[]>(x => x.Embedding, lambdaOptions, collectionName: "lambda_vectors").Should().BeTrue();
            var lambdaMetadata = GetVectorMetadata(db, "lambda_vectors", "Embedding");
            lambdaMetadata.Should().NotBeNull();
            lambdaMetadata!.Metric.Should().Be((byte)lambdaOptions.Metric);

            var lambdaNamedOptions = new VectorIndexOptions(2);
            repository.EnsureIndex<VectorDocument, float[]>("repo_lambda", x => x.Embedding, lambdaNamedOptions, collectionName: "lambda_named").Should().BeTrue();
            var lambdaNamedMetadata = GetVectorMetadata(db, "lambda_named", "repo_lambda");
            lambdaNamedMetadata.Should().NotBeNull();
            lambdaNamedMetadata!.Dimensions.Should().Be((ushort)lambdaNamedOptions.Dimensions);
        }

        [Fact]
        public void QueryExtensions_HonorMetricOverridesAndDeterministicOrdering()
        {
            using var db = CreateDatabase();
            var collection = db.GetCollection<VectorDocument>("vectors");
            collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(2));

            var documents = new[]
            {
                new VectorDocument { Id = 1, Embedding = new[] { 1f, 0f } },
                new VectorDocument { Id = 2, Embedding = new[] { 0.75f, 0.25f } },
                new VectorDocument { Id = 3, Embedding = new[] { 0.75f, 0.25f } },
                new VectorDocument { Id = 4, Embedding = new[] { -1f, 0f } }
            };

            collection.InsertBulk(documents);

            var queryVector = new[] { 1f, 0f };

            // WhereNear fallback with explicit metric override (Euclidean)
            var euclideanMatches = collection
                .Query()
                .WhereNear(x => x.Embedding, queryVector, maxDistance: 0.6, metric: VectorDistanceMetric.Euclidean)
                .ToEnumerable()
                .Select(x => x.Id)
                .ToList();

            euclideanMatches.Should().BeEquivalentTo(new[] { 1, 2, 3 });

            // TopKNear with maxDistance should trim results after ordering
            var topMatches = collection
                .Query()
                .TopKNear(
                    field: x => x.Embedding,
                    target: queryVector,
                    k: 2,
                    metric: VectorDistanceMetric.Cosine,
                    maxDistance: 0.25)
                .ToEnumerable()
                .Select(x => x.Id)
                .ToList();

            topMatches.Should().Equal(1, 2);

            // OrderByNearest must break ties deterministically on _id
            var orderedIds = collection
                .Query()
                .OrderByNearest(x => x.Embedding, queryVector)
                .ToEnumerable()
                .Select(x => x.Id)
                .ToList();

            orderedIds.Should().Equal(1, 2, 3, 4);

            // Nearest + WithVectorScore should surface similarity for dot-product metric
            collection.EnsureIndex("dot_vectors_idx", x => x.Embedding, new VectorIndexOptions(2, VectorDistanceMetric.DotProduct));

            var dotMatches = collection
                .Query()
                .Nearest(
                    field: x => x.Embedding,
                    target: queryVector,
                    k: 2,
                    metric: VectorDistanceMetric.DotProduct)
                .WithVectorScore(VectorScoreKind.Similarity)
                .ToList();

            dotMatches.Should().HaveCount(2);
            dotMatches.Select(m => m.Document.Id).Should().Equal(1, 2);
            dotMatches[0].Similarity.Should().BeApproximately(1d, 1e-6);
            dotMatches[1].Similarity.Should().NotBeNull();

            // Similarity projection on unsupported metric should throw
            Action similarityOnEuclidean = () => collection
                .Query()
                .WhereNear(
                    x => x.Embedding,
                    queryVector,
                    maxDistance: 0.6,
                    metric: VectorDistanceMetric.Euclidean)
                .WithVectorScore(VectorScoreKind.Similarity)
                .ToList();

            similarityOnEuclidean.Should().Throw<LiteException>();
        }

        private static LiteDatabase CreateDatabase()
        {
            return new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
        }

        private static VectorIndexMetadata? GetVectorMetadata(LiteDatabase db, string collection, string indexName)
        {
            var engine = (LiteEngine)EngineField.GetValue(db);
            var method = AutoTransactionMethod.MakeGenericMethod(typeof(VectorIndexMetadata));

            var result = method.Invoke(engine, new object[]
            {
                new Func<TransactionService, VectorIndexMetadata>(transaction =>
                {
                    var snapshot = transaction.CreateSnapshot(LockMode.Read, collection, false);
                    var metadataBuffer = snapshot.CollectionPage.GetVectorIndexMetadata(indexName);
                    return metadataBuffer != null ? VectorIndexMetadata.Wrap(metadataBuffer) : null;
                })
            });

            return (VectorIndexMetadata?)result;
        }
    }
}
