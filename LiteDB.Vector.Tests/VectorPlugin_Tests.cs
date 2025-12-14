using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Vector.Tests
{
    public class VectorPlugin_Tests
    {
        [Fact]
        public void VectorPlugin_Must_Be_Registered_For_VectorIndexing()
        {
            var file = CreateTempDatabasePath();

            try
            {
                using (var db = new LiteDatabase(file))
                {
                    var collection = db.GetCollection<TestDocument>("docs");
                    var exception = Assert.Throws<LiteException>(() =>
                        collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3)));

                    exception.Message.Should().Contain("Vector index support requires the LiteDB.Vector plugin");
                }

                using (var db = new LiteDatabase(file, plugins: new[] { VectorSearchPlugin.Instance }))
                {
                    var collection = db.GetCollection<TestDocument>("docs");
                    collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();

                    collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 1f, 0f, 0f } });
                    collection.Insert(new TestDocument { Id = 2, Embedding = new[] { 0f, 1f, 0f } });

                    var matches = collection.Query()
                        .WhereNear(x => x.Embedding, new[] { 1f, 0f, 0f }, maxDistance: 0.25)
                        .ToArray();

                    matches.Should().ContainSingle(doc => doc.Id == 1);
                }
            }
            finally
            {
                DeleteIfExists(file);
            }
        }

        [Fact]
        public void Plugin_Opens_Legacy_Vector_Databases()
        {
            var file = CreateTempDatabasePath();

            try
            {
                using (var db = new LiteDatabase(file, plugins: new[] { VectorSearchPlugin.Instance }))
                {
                    var collection = db.GetCollection<TestDocument>("docs");
                    collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3)).Should().BeTrue();

                    collection.Insert(new TestDocument { Id = 1, Embedding = new[] { 1f, 0f, 0f } });
                    collection.Insert(new TestDocument { Id = 2, Embedding = new[] { 0.7f, 0.2f, 0f } });
                    collection.Insert(new TestDocument { Id = 3, Embedding = new[] { 0f, 1f, 0f } });
                }

                using (var db = new LiteDatabase(file, plugins: new[] { VectorSearchPlugin.Instance }))
                {
                    var collection = db.GetCollection<TestDocument>("docs");
                    collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(3)).Should().BeFalse();

                    var nearest = collection.Query()
                        .TopKNear(x => x.Embedding, new[] { 1f, 0f, 0f }, 1)
                        .First();

                    nearest.Id.Should().Be(1);
                }
            }
            finally
            {
                DeleteIfExists(file);
            }
        }

        private static string CreateTempDatabasePath()
        {
            return Path.Combine(Path.GetTempPath(), $"vector-plugin-{Guid.NewGuid():N}.db");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private class TestDocument
        {
            public int Id { get; set; }

            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
