using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
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

        [Fact]
        public void Factory_should_invoke_handle_lifecycle_hooks_per_handle()
        {
            var plugin = new HandleLifecyclePlugin();

            using var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .UsePlugin(plugin)
                .BuildFactory();

            using var handle1 = factory.CreateDatabase();
            using var handle2 = factory.CreateDatabase();

            plugin.InitializeCount.Should().Be(1);
            plugin.HandleCreatedCount.Should().Be(2);
        }

        [Fact]
        public async Task CreateDatabase_should_be_race_safe_with_dispose()
        {
            var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .BuildFactory();

            var handles = new ConcurrentBag<ILiteDatabase>();
            var exceptions = new ConcurrentQueue<Exception>();

            var tasks = Enumerable.Range(0, 50)
                .Select(_ => Task.Run(() =>
                {
                    try
                    {
                        handles.Add(factory.CreateDatabase());
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                }))
                .ToArray();

            factory.Dispose();

            await Task.WhenAll(tasks);

            exceptions.Should().OnlyContain(ex => ex is ObjectDisposedException);

            foreach (var handle in handles)
            {
                handle.Dispose();
                handle.Dispose();
            }

            factory.Dispose();
        }

        private sealed class TrackingPlugin : ILitePlugin
        {
            public int InitializeCount { get; private set; }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                InitializeCount++;
            }
        }

        private sealed class HandleLifecyclePlugin : ILitePlugin, ILiteDatabaseHandleLifecycle
        {
            public int InitializeCount { get; private set; }

            public int HandleCreatedCount { get; private set; }

            public void Initialize(LiteDatabase database, ILitePluginContext context)
            {
                InitializeCount++;
            }

            public void OnHandleCreated(ILiteDatabase database)
            {
                HandleCreatedCount++;
            }
        }
    }
}
