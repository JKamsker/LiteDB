using System;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Vector.Tests.Integration
{
    public class FluentAPI_Tests
    {
        [Fact]
        public void WithVectorScore_ReusesPlannerScores_RespectsMaxDistance_AndIsDeterministic()
        {
            using var db = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
            var collection = db.GetCollection<VectorDocument>("vectors");
            collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(2));

            collection.InsertBulk(new[]
            {
                new VectorDocument { Id = 1, Embedding = new[] { 1f, 0f } },
                new VectorDocument { Id = 2, Embedding = new[] { 0.9f, 0.1f } },
                new VectorDocument { Id = 3, Embedding = new[] { 0.9f, -0.1f } },
                new VectorDocument { Id = 4, Embedding = new[] { -1f, 0f } }
            });

            var queryVector = new[] { 1f, 0f };

            var matches = collection
                .Query()
                .OrderByNearest(x => x.Embedding, queryVector)
                .WithVectorScore()
                .ToList();

            matches.Select(m => m.Document.Id).Should().Equal(1, 2, 3, 4);
            matches[0].Distance.Should().BeApproximately(0d, 1e-6);
            matches[1].Distance.Should().BeLessThan(matches[3].Distance);

            var cappedMatches = collection
                .Query()
                .OrderByNearest(x => x.Embedding, queryVector, maxDistance: 0.5)
                .WithVectorScore()
                .ToList();

            cappedMatches.Select(m => m.Document.Id).Should().Equal(1, 2, 3);
            cappedMatches.Should().OnlyContain(m => m.Distance <= 0.5);

            collection.Insert(new VectorDocument { Id = 5, Embedding = new[] { 0.9f, 0.1f } });

            var deterministicOrder = collection
                .Query()
                .OrderByNearest(x => x.Embedding, queryVector)
                .WithVectorScore()
                .ToEnumerable()
                .Select(m => m.Document.Id)
                .ToList();

            deterministicOrder.Should().Contain(2);
            deterministicOrder.Should().Contain(5);
            deterministicOrder.IndexOf(2).Should().BeLessThan(deterministicOrder.IndexOf(5));
        }

        private class VectorDocument
        {
            public int Id { get; set; }
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
