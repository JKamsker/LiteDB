using System;
using System.Linq;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Bson;
using LiteDB.Vector;
using LiteDB.Vector.Document;
using Xunit;

namespace LiteDB.Vector.Tests.Integration
{
    public class PluginContextIsolation_Tests
    {
        [Fact]
        public void VectorPlugin_Registers_Only_Against_Provided_Context()
        {
            var defaultContext = PluginContextFallbacks.Context;

            var defaultBsonTypes = defaultContext.BsonTypes.Registered.Count;
            var defaultPageTypes = defaultContext.PageFactories.Registered.Count;
            var defaultOperators = defaultContext.QueryOperators.Registered.Count;
            var defaultCostModels = defaultContext.QueryCostModels.Registered.Count;

            var pluginContext = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);

            using var database = new LiteDatabase(":memory:");

            VectorSearchPlugin.Instance.Initialize(database, pluginContext);

            pluginContext.BsonTypes.Registered.Should().Contain(descriptor => descriptor.TypeCode == VectorBsonConstants.TypeCode);
            pluginContext.PageFactories.Registered.Should().Contain(registration => registration.NumericCode == VectorPlugin.PageTypeCode);
            pluginContext.QueryOperators.Registered.Should().Contain(registration => registration.OperatorName == "VECTOR_KNN");
            pluginContext.QueryCostModels.Registered.Should().NotBeEmpty();

            defaultContext.BsonTypes.Registered.Count.Should().Be(defaultBsonTypes);
            defaultContext.PageFactories.Registered.Count.Should().Be(defaultPageTypes);
            defaultContext.QueryOperators.Registered.Count.Should().Be(defaultOperators);
            defaultContext.QueryCostModels.Registered.Count.Should().Be(defaultCostModels);
        }

        [Fact]
        public void VectorBsonType_Isolated_Per_Database_Context()
        {
            var document = new BsonDocument
            {
                ["_id"] = 1,
                ["vector"] = new BsonVector(new float[] { 1f, 2f, 3f })
            };

            using var databaseWithPlugin = new LiteDatabase(":memory:", plugins: new[] { VectorSearchPlugin.Instance });
            var collectionWithPlugin = databaseWithPlugin.GetCollection("docs");

            collectionWithPlugin.Insert(document).Should().Be(1);
            collectionWithPlugin.FindById(1)["vector"].Should().BeOfType<BsonVector>();

            using var databaseWithoutPlugin = new LiteDatabase(":memory:");
            var collectionWithoutPlugin = databaseWithoutPlugin.GetCollection("docs");

            var exception = Assert.Throws<NotSupportedException>(() => collectionWithoutPlugin.Insert(document));

            exception.Message.Should().Contain("BSON type").And.Contain(((byte)VectorBsonConstants.TypeCode).ToString());
        }
    }
}
