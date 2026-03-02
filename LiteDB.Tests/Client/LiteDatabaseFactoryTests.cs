using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class LiteDatabaseFactoryTests
    {
        private sealed class TrackingDisposable : IDisposable
        {
            private int _disposeCount;

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        private sealed class TestPluginHostEngine : ILiteEngine, IPluginHost
        {
            private int _disposeCount;

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public bool ThrowOnSetPluginContext { get; set; }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }

            void IPluginHost.SetPluginContext(ILitePluginContext context)
            {
                if (this.ThrowOnSetPluginContext)
                {
                    throw new InvalidOperationException("Test engine failure in SetPluginContext.");
                }
            }

            public int Checkpoint() => throw new NotSupportedException();
            public long Rebuild(RebuildOptions options) => throw new NotSupportedException();
            public bool BeginTrans() => throw new NotSupportedException();
            public bool Commit() => throw new NotSupportedException();
            public bool Rollback() => throw new NotSupportedException();
            public IBsonDataReader Query(string collection, Query query) => throw new NotSupportedException();
            public int Insert(string collection, System.Collections.Generic.IEnumerable<BsonDocument> docs, BsonAutoId autoId) => throw new NotSupportedException();
            public int Update(string collection, System.Collections.Generic.IEnumerable<BsonDocument> docs) => throw new NotSupportedException();
            public int UpdateMany(string collection, BsonExpression transform, BsonExpression predicate) => throw new NotSupportedException();
            public int Upsert(string collection, System.Collections.Generic.IEnumerable<BsonDocument> docs, BsonAutoId autoId) => throw new NotSupportedException();
            public int Delete(string collection, System.Collections.Generic.IEnumerable<BsonValue> ids) => throw new NotSupportedException();
            public int DeleteMany(string collection, BsonExpression predicate) => throw new NotSupportedException();
            public bool DropCollection(string name) => throw new NotSupportedException();
            public bool RenameCollection(string name, string newName) => throw new NotSupportedException();
            public bool EnsureIndex(string collection, string name, BsonExpression expression, bool unique) => throw new NotSupportedException();
            public bool EnsureCustomIndex(string collection, string name, string strategyKind, BsonExpression expression, BsonDocument options) => throw new NotSupportedException();
            public bool DropIndex(string collection, string name) => throw new NotSupportedException();
            public BsonValue Pragma(string name) => throw new NotSupportedException();
            public bool Pragma(string name, BsonValue value) => throw new NotSupportedException();
        }

        [Fact]
        public void BuildFactory_should_share_engine_across_handles()
        {
            var plugin = new TestTrackingPlugin();

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
        public void Factory_cleanup_should_run_when_unreferenced_handle_is_finalized()
        {
            var engine = new TestPluginHostEngine();
            var ownedResources = new TrackingDisposable();
            var pluginContext = new DefaultPluginContext(new ConnectionString(), null, null);

            using var factory = new LiteDatabaseFactory(
                engine,
                ownsEngine: true,
                mapper: BsonMapper.Global,
                pluginContext: pluginContext,
                plugins: Array.Empty<ILitePlugin>(),
                ownedResources: ownedResources);

            var handleRef = CreateUnreleasedHandle(factory);

            factory.Dispose();

            ForceGarbageCollection();

            handleRef.IsAlive.Should().BeFalse();
            engine.DisposeCount.Should().Be(1);
            ownedResources.DisposeCount.Should().Be(1);

            static void ForceGarbageCollection()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnreleasedHandle(ILiteDatabaseFactory factory)
        {
            var handle = factory.CreateDatabase();
            return new WeakReference(handle);
        }

        [Fact]
        public void Factory_should_refuse_new_handles_after_disposal()
        {
            using var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .BuildFactory();

            factory.Dispose();

            Action act = () => factory.CreateDatabase();

            act.Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Factory_should_invoke_handle_lifecycle_hooks_per_handle()
        {
            var plugin = new TestTrackingPlugin();

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
        public void Factory_handles_should_have_frozen_registries()
        {
            using var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .BuildFactory();

            using var handle = (LiteDatabase)factory.CreateDatabase();

            Action act = () => handle.Services.ExpressionRegistry.RegisterKeyword("AFTER_INIT");

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Factory_handles_should_refuse_rebuild()
        {
            using var factory = new LiteDatabaseBuilder()
                .UseInMemory()
                .BuildFactory();

            using var handle1 = (LiteDatabase)factory.CreateDatabase();
            using var handle2 = factory.CreateDatabase();

            Action act = () => handle1.Rebuild();

            act.Should().Throw<InvalidOperationException>();

            handle2.GetCollection<BsonDocument>("docs").Insert(new BsonDocument { ["_id"] = 1 });
            handle2.GetCollection<BsonDocument>("docs").Count().Should().Be(1);
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

            exceptions.Should().NotContain(ex => !(ex is ObjectDisposedException));

            foreach (var handle in handles)
            {
                handle.Dispose();
                handle.Dispose();
            }

            factory.Dispose();
        }

        [Fact]
        public async Task CreateDatabase_rollback_should_run_cleanup_path_when_disposed_concurrently()
        {
            var engine = new TestPluginHostEngine();
            var ownedResources = new TrackingDisposable();
            var pluginContext = new DefaultPluginContext(new ConnectionString(), null, null);

            using var factory = new LiteDatabaseFactory(
                engine,
                ownsEngine: true,
                mapper: BsonMapper.Global,
                pluginContext: pluginContext,
                plugins: Array.Empty<ILitePlugin>(),
                ownedResources: ownedResources);

            using var entered = new ManualResetEventSlim(false);
            using var allowContinue = new ManualResetEventSlim(false);

            factory.AfterRefCountIncrementForTesting = () =>
            {
                entered.Set();
                allowContinue.Wait();
            };

            Exception exception = null;

            var task = Task.Run(() =>
            {
                try
                {
                    factory.CreateDatabase();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            entered.Wait();

            factory.Dispose();

            allowContinue.Set();

            await task;

            exception.Should().BeOfType<ObjectDisposedException>();
            engine.DisposeCount.Should().Be(1);
            ownedResources.DisposeCount.Should().Be(1);
        }

        [Fact]
        public void CreateDatabase_should_release_lease_when_handle_constructor_throws()
        {
            var engine = new TestPluginHostEngine();
            var ownedResources = new TrackingDisposable();
            var pluginContext = new DefaultPluginContext(new ConnectionString(), null, null);

            using var factory = new LiteDatabaseFactory(
                engine,
                ownsEngine: true,
                mapper: BsonMapper.Global,
                pluginContext: pluginContext,
                plugins: Array.Empty<ILitePlugin>(),
                ownedResources: ownedResources);

            engine.ThrowOnSetPluginContext = true;

            Action act = () => factory.CreateDatabase();

            act.Should().Throw<InvalidOperationException>();

            factory.Dispose();

            engine.DisposeCount.Should().Be(1);
            ownedResources.DisposeCount.Should().Be(1);
        }

    }
}
