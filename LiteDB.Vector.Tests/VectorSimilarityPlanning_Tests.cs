using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Vector.Tests
{
    public class VectorSimilarityPlanning_Tests
    {
        [Fact]
        public void Planner_uses_vector_index_for_similarity_predicate()
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
            var collection = db.GetCollection<TestDocument>("docs");

            collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 1.0f, 0.0f } });
            collection.Insert(new TestDocument { Id = 2, Embedding = new[] { 0.0f, 1.0f } });

            collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(2)).Should().BeTrue();

            var predicate = BsonExpression.Create(
                "VECTOR_SIM($.Embedding, [1.0, 0.0]) >= 0.9",
                db.Services.ExpressionRegistry);

            var plan = collection.Query()
                .Where(predicate)
                .GetPlan();

            plan["index"]["name"].AsString.Should().Be("embedding_idx");
            plan["index"]["mode"].AsString.Should().Be("VECTOR INDEX SEARCH");
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}

