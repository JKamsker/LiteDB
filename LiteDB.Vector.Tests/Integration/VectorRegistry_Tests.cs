using System.IO;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Vector.Tests.Integration
{
    public class VectorRegistry_Tests
    {
        [Fact]
        public void Plugin_Should_Expose_Extension_Point_Registrations()
        {
            using var db = new LiteDatabase(new MemoryStream(), plugins: new[] { VectorSearchPlugin.Instance });

            var context = db.Services.Context;

            context.QueryMetadata.TryGetDescriptor("LiteDB.Vector", out var metadataDescriptor).Should().BeTrue();
            metadataDescriptor.Should().NotBeNull();
            metadataDescriptor.Version.Should().Be(1);
            metadataDescriptor.ReservedKeys.Should().BeEquivalentTo(new[] { "VectorField", "TargetEmbedding", "VectorMaxDistance", "VectorMetric" });

            context.PageFactories.TryGet("VectorIndex", out var pageFactory).Should().BeTrue();
            pageFactory.Should().NotBeNull();
            pageFactory.PluginId.Should().Be("LiteDB.Vector");
            pageFactory.PageType.Should().Be("VectorIndex");

            context.VectorIndexes.Registered.Should().NotBeEmpty();
            var strategy = context.VectorIndexes.Registered.FirstOrDefault(x => x.StrategyId == "LiteDB.Vector");
            strategy.Should().NotBeNull();
            strategy.PluginId.Should().Be("LiteDB.Vector");
            strategy.RequiredPageTypes.Should().Contain("VectorIndex");
            strategy.RequiredBsonTypes.Should().Contain((byte)BsonType.Vector);
        }
    }
}
