using System;
using FluentAssertions;
using LiteDB.Plugins.Query;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class QueryMetadataBagTests
    {
        private const string PluginId = "TestPlugin";

        [Fact]
        public void GetOrCreateMetadata_ShouldReturnDescriptorBackedBag_WhenDescriptorExists()
        {
            var _ = LiteDatabaseServices.Default;
            var descriptor = new QueryMetadataDescriptor(PluginId, version: 2, reservedKeys: new[] { "AllowedKey" });
            var query = new Query();

            var bag = query.GetOrCreateMetadata(PluginId, () => new QueryMetadataBag(descriptor));

            bag.PluginId.Should().Be(PluginId);
            bag.Version.Should().Be(2);
            bag.Set("AllowedKey", 42);
            bag.TryGet<int>("AllowedKey", out var stored).Should().BeTrue();
            stored.Should().Be(42);
        }

        [Fact]
        public void GetOrCreateMetadata_ShouldFallback_WhenDescriptorMissing()
        {
            var _ = LiteDatabaseServices.Default;
            var query = new Query();

            var fallback = query.GetOrCreateMetadata(PluginId, () => new QueryMetadataBag(PluginId, version: 5, reservedKeys: Array.Empty<string>()));

            fallback.PluginId.Should().Be(PluginId);
            fallback.Version.Should().Be(5);
            fallback.Set("dynamic", "value");
            fallback.Get<string>("dynamic").Should().Be("value");
        }

    }
}
