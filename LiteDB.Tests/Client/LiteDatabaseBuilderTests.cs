using FluentAssertions;
using LiteDB;
using LiteDB.Plugins;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class LiteDatabaseBuilderTests
    {
        [Fact]
        public void Build_should_create_database_and_initialize_plugins()
        {
            using var file = new TempFile();
            var plugin = new TrackingPlugin();

            using (var db = new LiteDatabaseBuilder()
                .UseFile(file.Filename)
                .UsePlugin(plugin)
                .Build())
            {
                plugin.InitializeCount.Should().Be(1);

                var collection = db.GetCollection<BsonDocument>("docs");
                collection.Insert(new BsonDocument { ["_id"] = 1 });
            }

            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
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
