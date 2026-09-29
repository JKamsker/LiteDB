using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using LiteDB.Client.Direct;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectPoolFinalizer_Tests
    {
        [Fact]
        public void Abandoned_owner_does_not_close_a_retained_owner()
        {
            using var file = new TempFile();
            using var retained = new LiteDatabase(file);
            retained.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            var abandoned = AbandonOwner(file);
            CollectUntil(() => !abandoned.IsAlive);
            Assert.True(NativeAdmissionDirectPool_Tests.Locked(file));
            retained.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
            retained.Dispose();
            CollectUntil(() => !NativeAdmissionDirectPool_Tests.Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.Equal(2, cold.GetCollection("rows").Count());
        }

        [Fact]
        public void Weak_registry_does_not_root_engine_callback_database_cycles()
        {
            using var file = new TempFile();
            var graph = AbandonCycle(file);
            CollectUntil(() => graph.All(weak => !weak.IsAlive) && !NativeAdmissionDirectPool_Tests.Locked(file));
            Assert.False(DirectEnginePool.Contains(file));
            using var cold = new LiteDatabase(file);
            Assert.Equal(42, cold.GetCollection("rows").FindById(1)["value"].AsInt32);
            Assert.Equal(1, cold.GetCollection("sentinel").Count());
        }

        [Fact]
        public void Live_collection_retains_engine_after_database_object_is_collected()
        {
            using var file = new TempFile();
            var graph = UseCollectionAfterDatabaseGc(file);
            CollectUntil(() => graph.All(weak => !weak.IsAlive) && !NativeAdmissionDirectPool_Tests.Locked(file));
            Assert.False(DirectEnginePool.Contains(file));
            using var cold = new LiteDatabase(file);
            Assert.Equal(2, cold.GetCollection("rows").Count());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AbandonOwner(string file)
        {
            var db = new LiteDatabase(file);
            return new WeakReference(db.Context.RawEngine);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] AbandonCycle(string file)
        {
            LiteDatabase db = null;
            var connection = new ConnectionString { Filename = file };
            var lease = connection.CreateEngine(settings => settings.ReadTransform = (collection, value) =>
            {
                GC.KeepAlive(db);
                return value;
            });
            db = new LiteDatabase(lease);
            Assert.True(DirectEnginePool.Contains(file));
            db.GetCollection("rows").EnsureIndex("value", true);
            db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 42 });
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
            Assert.Equal(1, db.GetCollection("rows").FindAll().Count());
            return new[] { new WeakReference(db), new WeakReference(lease), new WeakReference(((DirectEngineLease)lease).Engine) };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] UseCollectionAfterDatabaseGc(string file)
        {
            var collection = CreateCollection(file, out var database);
            CollectUntil(() => !database.IsAlive);
            Assert.True(NativeAdmissionDirectPool_Tests.Locked(file));
            collection.Insert(new BsonDocument { ["_id"] = 2 });
            Assert.Equal(2, collection.Count());
            return new[] { new WeakReference(collection) };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ILiteCollection<BsonDocument> CreateCollection(string file, out WeakReference database)
        {
            var db = new LiteDatabase(file);
            Assert.True(DirectEnginePool.Contains(file));
            var collection = db.GetCollection("rows");
            collection.Insert(new BsonDocument { ["_id"] = 1 });
            database = new WeakReference(db);
            return collection;
        }

        private static void CollectUntil(Func<bool> complete)
        {
            for (var i = 0; i < 20; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (complete()) return;
                Thread.Sleep(25);
            }
            Assert.True(complete(), "The abandoned graph must release native exclusion after finalization.");
        }
    }
}
