using System;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class LiteDatabaseFactoryTests
    {
        [Fact]
        public void BuildFactory_should_share_engine_across_handles()
        {
            var plugin = new TrackingPlugin();

            using var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .UsePlugin(plugin)
                .BuildFactory();

            plugin.InitializeCount.Should().Be(1);

            using (var db1 = factory.CreateDatabase())
            {
                db1.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
            }

            using (var db2 = factory.CreateDatabase())
            {
                db2.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
            }
        }

        [Fact]
        public void Handles_should_outlive_factory_disposal()
        {
            using var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .BuildFactory();

            using var handle = factory.CreateDatabase();

            factory.Dispose();

            handle.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
            handle.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
        }

        private sealed class TrackingPlugin : ILitePlugin
        {
            public int InitializeCount { get; private set; }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                InitializeCount++;
            }
        }
    }
}

