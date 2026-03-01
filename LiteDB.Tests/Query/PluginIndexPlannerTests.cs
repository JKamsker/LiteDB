using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Tests.QueryTest
{
    public class PluginIndexPlannerTests
    {
        [Fact]
        public void Planner_should_ignore_plugin_indexes_in_orderby_fallback()
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
            var collection = db.GetCollection<TestDocument>("docs");

            collection.Insert(new[]
            {
                new TestDocument { Id = 1, Embedding = new[] { 0.1f, 0.2f, 0.3f } },
                new TestDocument { Id = 2, Embedding = new[] { 0.3f, 0.2f, 0.1f } }
            });

            collection.EnsureIndex("embedding_idx", x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();

            var plan = collection.Query()
                .OrderBy(x => x.Embedding)
                .GetPlan();

            plan["index"]["name"].AsString.Should().Be("_id");
        }

        private sealed class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; }
        }
    }
}
