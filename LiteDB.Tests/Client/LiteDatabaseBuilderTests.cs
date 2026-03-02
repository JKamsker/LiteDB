using System;
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

        [Fact]
        public void Build_should_throw_when_called_twice()
        {
            var builder = new LiteDatabaseBuilder().UseInMemory();

            using var db = builder.Build();

            Action act = () => builder.Build();

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void UseFile_should_throw_when_another_data_source_is_already_configured()
        {
            var builder = new LiteDatabaseBuilder().UseInMemory();

            Action act = () => builder.UseFile("other.db");

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Build_should_throw_when_no_data_source_is_configured()
        {
            var builder = new LiteDatabaseBuilder();

            Action act = () => builder.Build();

            act.Should().Throw<InvalidOperationException>();
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
