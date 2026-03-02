using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Bson;
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
            return new CustomBsonTypeDescriptor(
                pluginId,
                typeCode,
                name: "TestVector",
                calculateSize: _ => 0,
                serializer: (_, __) => { },
                deserializer: _ => BsonValue.Null,
                jsonFormatter: _ => "null");
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
