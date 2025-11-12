using System.Collections.Generic;
using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using Xunit;

namespace LiteDB.Tests.Utils
{
    public class DatabaseFactoryTests
    {
        [Fact]
        public void CreateMany_ShouldCreateIsolatedInMemoryDatabases()
        {
            using var databases = DatabaseFactory.CreateMany(
                DatabaseFactoryOptions.InMemory(),
                DatabaseFactoryOptions.InMemory());

            databases.Count.Should().Be(2);

            var first = databases[0];
            var second = databases[1];

            first.Should().NotBeSameAs(second);

            var firstCollection = first.GetCollection<BsonDocument>("items");
            var secondCollection = second.GetCollection<BsonDocument>("items");

            firstCollection.Insert(new BsonDocument { ["_id"] = 1 });

            firstCollection.Count().Should().Be(1);
            secondCollection.Count().Should().Be(0);
        }

        [Fact]
        public void CreateMany_ShouldHonorPerDatabasePluginConfiguration()
        {
            var trackingPlugin = new TrackingPlugin();

            using var databases = DatabaseFactory.CreateMany(
                DatabaseFactoryOptions.InMemory(plugins: new[] { trackingPlugin }),
                DatabaseFactoryOptions.InMemory());

            trackingPlugin.InitializeCount.Should().Be(1);
            trackingPlugin.Databases.Should().Contain(databases[0]);
            trackingPlugin.Databases.Should().NotContain(databases[1]);
        }

        private sealed class TrackingPlugin : ILitePlugin
        {
            private readonly List<LiteDatabase> _databases = new List<LiteDatabase>();

            public int InitializeCount { get; private set; }

            public IReadOnlyCollection<LiteDatabase> Databases => _databases.AsReadOnly();

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                InitializeCount++;
                _databases.Add(database);
            }
        }
    }
}
