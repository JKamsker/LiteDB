using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Bson;
using LiteDB.Plugins.Indexing;
using LiteDB.Plugins.Query;
using LiteDB.Plugins.Storage;
using Xunit;

namespace LiteDB.Tests.Plugins
{
    public class ReservedIdentifierConflictTests
    {
        [Fact]
        public void CustomBsonRegistryShouldRejectVectorReservedCodesFromOtherPlugins()
        {
            var registry = new CustomBsonTypeRegistry();
            var descriptor = CreateBsonDescriptor("ThirdParty.Plugin", ReservedCodeRanges.VectorBsonStart);

            Action act = () => registry.Register(descriptor);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Theory]
        [InlineData(ReservedCodeRanges.VectorBsonStart)]
        [InlineData(ReservedCodeRanges.VectorBsonEnd)]
        public void CustomBsonRegistryShouldRejectVectorReservedAliasesFromOtherPlugins(byte reservedAlias)
        {
            var registry = new CustomBsonTypeRegistry();
            var descriptor = CreateBsonDescriptor("ThirdParty.Plugin", typeCode: 0x80, legacyAliases: new[] { reservedAlias });

            Action act = () => registry.Register(descriptor);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Fact]
        public void CustomBsonRegistryShouldRejectAliasesThatOverlapTypeCodes()
        {
            var registry = new CustomBsonTypeRegistry();
            var descriptor = CreateBsonDescriptor("Plugin.A", typeCode: 0x80);
            var conflicting = CreateBsonDescriptor("Plugin.B", typeCode: 0x81, legacyAliases: new[] { (byte)0x80 });

            registry.Register(descriptor);

            Action act = () => registry.Register(conflicting);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*Plugin.A*Plugin.B*");
        }

        [Fact]
        public void CustomBsonRegistryShouldRejectTypeCodesThatOverlapAliases()
        {
            var registry = new CustomBsonTypeRegistry();
            var descriptor = CreateBsonDescriptor("Plugin.A", typeCode: 0x80, legacyAliases: new[] { (byte)0x81 });
            var conflicting = CreateBsonDescriptor("Plugin.B", typeCode: 0x81);

            registry.Register(descriptor);

            Action act = () => registry.Register(conflicting);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*Plugin.A*Plugin.B*");
        }

        [Fact]
        public void QueryMetadataRegistryShouldRejectDuplicatePluginIds()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);

            context.RegisterQueryMetadata("ThirdParty.Plugin", version: 1, reservedKeys: new[] { "key1" });

            Action act = () => context.RegisterQueryMetadata("ThirdParty.Plugin", version: 2, reservedKeys: new[] { "key1", "key2" });

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*ThirdParty.Plugin*");
        }

        [Fact]
        public void QueryMetadataRegistryShouldAllowIdempotentRegistration()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);

            context.RegisterQueryMetadata("ThirdParty.Plugin", version: 1, reservedKeys: new[] { "key1" });

            Action act = () => context.RegisterQueryMetadata("ThirdParty.Plugin", version: 1, reservedKeys: new[] { "key1" });

            act.Should().NotThrow();
        }

        [Fact]
        public void IndexMetadataRegistryShouldRejectVectorReservedKindsFromOtherPlugins()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
            var descriptor = new PluginIndexMetadataDescriptor(
                pluginId: "ThirdParty.Plugin",
                indexKind: ReservedCodeRanges.VectorIndexKind,
                serialize: _ => Array.Empty<byte>(),
                deserialize: _ => new BsonDocument());

            Action act = () => context.IndexMetadata.Register(descriptor);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Fact]
        public void IndexMetadataRegistryShouldRejectVectorReservedPrefixesFromOtherPlugins()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
            var descriptor = new PluginIndexMetadataDescriptor(
                pluginId: "ThirdParty.Plugin",
                indexKind: "vector.ivf",
                serialize: _ => Array.Empty<byte>(),
                deserialize: _ => new BsonDocument());

            Action act = () => context.IndexMetadata.Register(descriptor);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Fact]
        public void QueryCostModelRegistryShouldRejectVectorReservedKindsFromOtherPlugins()
        {
            var registry = new QueryCostModelRegistry();
            var registration = new QueryCostModelRegistration(
                pluginId: "ThirdParty.Plugin",
                indexKind: ReservedCodeRanges.VectorIndexKind,
                calculateCost: _ => 1d);

            Action act = () => registry.Register(registration);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Fact]
        public void QueryCostModelRegistryShouldRejectVectorReservedPrefixesFromOtherPlugins()
        {
            var registry = new QueryCostModelRegistry();
            var registration = new QueryCostModelRegistration(
                pluginId: "ThirdParty.Plugin",
                indexKind: "vector.ivf",
                calculateCost: _ => 1d);

            Action act = () => registry.Register(registration);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Fact]
        public void VectorPluginShouldRegisterReservedIndexKindSuccessfully()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
            var descriptor = new PluginIndexMetadataDescriptor(
                pluginId: ReservedCodeRanges.VectorPluginId,
                indexKind: ReservedCodeRanges.VectorIndexKind,
                serialize: _ => Array.Empty<byte>(),
                deserialize: _ => new BsonDocument
                {
                    ["kind"] = ReservedCodeRanges.VectorIndexKind
                });

            context.IndexMetadata.Register(descriptor);

            context.IndexMetadata.TryGet(ReservedCodeRanges.VectorIndexKind.ToUpperInvariant(), out var resolved)
                .Should().BeTrue();
            resolved.PluginId.Should().Be(ReservedCodeRanges.VectorPluginId);
            resolved.IndexKind.Should().Be(ReservedCodeRanges.VectorIndexKind);
        }

        [Fact]
        public void PageTypeRegistryShouldRejectVectorReservedCodesFromOtherPlugins()
        {
            var registry = new PageTypeRegistry();
            var registration = CreatePageRegistration("Another.Plugin", ReservedCodeRanges.VectorPageStart);

            Action act = () => registry.Register(registration);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*LiteDB.Vector*");
        }

        [Fact]
        public void VectorPluginShouldRegisterReservedCodesSuccessfully()
        {
            var bsonRegistry = new CustomBsonTypeRegistry();
            var pageRegistry = new PageTypeRegistry();

            var vectorBson = CreateBsonDescriptor(ReservedCodeRanges.VectorPluginId, ReservedCodeRanges.VectorBsonStart);
            var vectorPage = CreatePageRegistration(ReservedCodeRanges.VectorPluginId, ReservedCodeRanges.VectorPageStart);

            bsonRegistry.Register(vectorBson);
            pageRegistry.Register(vectorPage);

            bsonRegistry.TryGetByTypeCode(ReservedCodeRanges.VectorBsonStart, out var resolvedBson)
                .Should().BeTrue();
            resolvedBson.Should().BeSameAs(vectorBson);

            pageRegistry.TryGet(ReservedCodeRanges.VectorPageStart, out var resolvedPage)
                .Should().BeTrue();
            resolvedPage.Should().BeSameAs(vectorPage);
        }

        [Theory]
        [InlineData((byte)0x00)]
        [InlineData((byte)0x01)]
        [InlineData((byte)0x02)]
        [InlineData((byte)0x03)]
        [InlineData((byte)0x04)]
        [InlineData((byte)0x7F)]
        public void PageTypeRegistryShouldRejectCorePageCodes(byte code)
        {
            var registry = new PageTypeRegistry();
            var registration = CreatePageRegistration("ThirdParty.Plugin", code);

            Action act = () => registry.Register(registration);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*reserved for core page types*");
        }

        private static CustomBsonTypeDescriptor CreateBsonDescriptor(string pluginId, byte typeCode)
        {
            return CreateBsonDescriptor(pluginId, typeCode, legacyAliases: null);
        }

        private static CustomBsonTypeDescriptor CreateBsonDescriptor(string pluginId, byte typeCode, byte[] legacyAliases)
        {
            return new CustomBsonTypeDescriptor(
                pluginId,
                typeCode,
                name: "TestVector",
                calculateSize: _ => 0,
                serializer: (_, __) => { },
                deserializer: _ => BsonValue.Null,
                jsonFormatter: _ => "null",
                legacyAliases: legacyAliases);
        }

        private static PageFactoryRegistration CreatePageRegistration(string pluginId, byte code)
        {
            return new PageFactoryRegistration(
                pluginId: pluginId,
                pageType: "VectorIndex",
                numericCode: code,
                compatibilityRange: ">=8.0",
                factory: ctx => new object());
        }
    }
}
