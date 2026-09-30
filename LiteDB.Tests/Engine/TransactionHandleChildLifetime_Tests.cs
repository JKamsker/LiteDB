#if !NETFRAMEWORK
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LiteDB.Engine;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleChildLifetime_Tests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("secret", false)]
        [InlineData("secret", true)]
        public void Failed_reused_open_or_core_close_discards_child_and_preserves_committed_data(string password, bool failOpen)
        {
            using var file = new TempFile();
            using (var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password }))
            using (var db = new LiteDatabase(shared, disposeOnClose: false))
            {
                using (var warm = db.BeginTransaction())
                {
                    warm.GetCollection("rows").EnsureIndex("value");
                    warm.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "kept" });
                    warm.Commit();
                }
                var cached = TransactionHandleChildReuse_Tests.Cached(shared);
                Assert.NotNull(cached);
                var failure = new IOException("reused child failure");
                if (failOpen)
                {
                    cached.SimulateOpenEngine = () => throw failure;
                    Assert.Same(failure, Assert.Throws<IOException>(() => db.BeginTransaction()));
                }
                else
                {
                    using var tx = db.BeginTransaction();
                    var core = TransactionHandleChildReuse_Tests.Core(tx);
                    var field = typeof(LiteEngine).GetField("_modeGuard", BindingFlags.Instance | BindingFlags.NonPublic);
                    field.SetValue(core, new FailAfterDispose((IDisposable)field.GetValue(core), failure));
                    Assert.Same(failure, Assert.Throws<IOException>(tx.Commit));
                    Assert.Equal(LiteTransactionState.Committed, tx.State);
                }
                Assert.Null(TransactionHandleChildReuse_Tests.Cached(shared));
                using var retry = db.BeginTransaction(TimeSpan.FromSeconds(5));
                Assert.NotNull(retry.GetCollection("rows").FindOne("value = 'kept'"));
                retry.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = "after failure" });
                retry.Commit();
                Assert.NotSame(cached, TransactionHandleChildReuse_Tests.Cached(shared));
            }
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Assert.Equal(2, cold.GetCollection("rows").Count());
                Assert.NotNull(cold.GetCollection("rows").FindOne("value = 'kept'"));
                Assert.NotNull(cold.GetCollection("rows").FindOne("value = 'after failure'"));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public void Reused_wrapper_after_failed_wal_write_reopens_without_unconfirmed_rows(string password)
        {
            using var file = new TempFile();
            using (var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password }))
            using (var db = new LiteDatabase(shared, disposeOnClose: false))
            {
                using (var warm = db.BeginTransaction())
                {
                    warm.GetCollection("rows").EnsureIndex("value");
                    warm.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "kept" });
                    warm.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                    warm.Commit();
                }
                var cached = TransactionHandleChildReuse_Tests.Cached(shared);
                Assert.NotNull(cached);
                using var failed = db.BeginTransaction();
                failed.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = "unconfirmed" });
                var core = TransactionHandleChildReuse_Tests.Core(failed);
                var reached = false;
                core.SimulateDiskWriteFail = page => { reached = true; throw new IOException("failed WAL write"); };
                Assert.Throws<IOException>(failed.Commit);
                Assert.True(reached);
                Assert.Equal(LiteTransactionState.Indeterminate, failed.State);
                using var retry = db.BeginTransaction(TimeSpan.FromSeconds(5));
                Assert.NotSame(core, TransactionHandleChildReuse_Tests.Core(retry));
                Assert.NotNull(retry.GetCollection("rows").FindOne("value = 'kept'"));
                Assert.Empty(retry.GetCollection("rows").Find("value = 'unconfirmed'"));
                retry.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["value"] = "after failure" });
                retry.Commit();
            }
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Assert.Equal(new[] { 1, 3 }, cold.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).OrderBy(id => id));
                Assert.NotNull(cold.GetCollection("rows").FindOne("value = 'kept'"));
                Assert.NotNull(cold.GetCollection("rows").FindOne("value = 'after failure'"));
                Assert.NotNull(cold.GetCollection("sentinel").FindById(9));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Retained_completed_handle_and_idle_worker_do_not_root_cached_or_abandoned_session(bool abandonActive)
        {
            using var file = new TempFile();
            SharedHolderScheduler.IdleWaitOverride = 60000;
            Thread worker = null;
            TransactionAdmission.Observe = stage => { if (stage == "storage-opened") worker = Thread.CurrentThread; };
            ILiteTransaction completed = null;
            WeakReference[] graph = null;
            try
            {
                TransactionHandle_Tests.OnThread(() => completed = Abandon(file, abandonActive, out graph));
                // For active abandonment this wait intentionally follows collection:
                // its holder must stop rooting the graph before its finalizer can signal.
                for (var attempt = 0; attempt < 100 &&
                    (graph.Any(reference => reference.IsAlive) || !SharedHolderScheduler.IsIdle(worker)); attempt++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(20);
                }
                Assert.All(graph, reference => Assert.False(reference.IsAlive));
                Assert.True(SharedHolderScheduler.IsIdle(worker));
                Assert.Equal(LiteTransactionState.Committed, completed.State);
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    using var cold = new LiteDatabase(file);
                    Assert.Equal(1, cold.GetCollection("rows").Count());
                    Assert.NotNull(cold.GetCollection("rows").FindOne("value = 'kept'"));
                    Assert.Null(cold.GetCollection("rows").FindById(2));
                }
                GC.KeepAlive(completed);
            }
            finally
            {
                completed?.Dispose();
                TransactionAdmission.Observe = null;
                SharedHolderScheduler.IdleWaitOverride = null;
            }
        }

        private sealed class ApplicationState
        {
            internal LiteDatabase Database;
            internal ILiteTransaction Active;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ILiteTransaction Abandon(string filename, bool abandonActive, out WeakReference[] graph)
        {
            var state = new ApplicationState();
            var ambient = new AsyncLocal<object> { Value = state };
            var shared = new SharedEngine(new EngineSettings { Filename = filename, TransactionPageLimit = 1,
                ReadTransform = (collection, value) => { GC.KeepAlive(state); return value; } });
            var db = state.Database = new LiteDatabase(shared);
            var completed = db.BeginTransaction();
            completed.GetCollection("rows").EnsureIndex("value");
            completed.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "kept" });
            Assert.NotNull(completed.GetCollection("rows").FindById(1));
            completed.Commit();
            var child = TransactionHandleChildReuse_Tests.Cached(shared);
            Assert.NotNull(child);
            if (abandonActive)
            {
                state.Active = db.BeginTransaction();
                state.Active.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["payload"] = new string('x', 50000) });
            }
            graph = new[] { new WeakReference(db), new WeakReference(shared), new WeakReference(child), new WeakReference(state) };
            GC.KeepAlive(ambient.Value);
            return completed;
        }

        private sealed class FailAfterDispose : IDisposable
        {
            private readonly IDisposable _inner;
            private readonly Exception _failure;
            internal FailAfterDispose(IDisposable inner, Exception failure) { _inner = inner; _failure = failure; }
            public void Dispose() { _inner?.Dispose(); throw _failure; }
        }
    }
}
#endif
