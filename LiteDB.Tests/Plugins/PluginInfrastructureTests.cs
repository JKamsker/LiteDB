using System.IO;
using FluentAssertions;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class PluginInfrastructureTests
    {
        private class NameDocument
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }

        [Fact]
        public void DefaultRegistries_ShouldBeAvailableWithoutPlugins()
        {
            using var db = new LiteDatabase(new MemoryStream());

            db.Services.ExpressionRegistry.Should().NotBeNull();
            db.Services.IndexRegistry.Should().NotBeNull();
            db.Services.QueryPlanner.Should().NotBeNull();
            db.Services.LinqResolvers.Should().NotBeNull();
            db.Services.IndexInterceptors.Should().NotBeNull();
        }

        [Fact]
        public void EnsureIndex_ShouldFallbackToDefaultWhenNoInterceptors()
        {
            using var db = new LiteDatabase(new MemoryStream());

            var collection = db.GetCollection<NameDocument>("docs");
            collection.Insert(new NameDocument { Id = 1, Name = "alpha" });

            var created = collection.EnsureIndex(x => x.Name);
            created.Should().BeTrue();

            var duplicate = collection.EnsureIndex(x => x.Name);
            duplicate.Should().BeFalse();

            collection.Query().Where(x => x.Name == "alpha").Count().Should().Be(1);
        }
    }
}
