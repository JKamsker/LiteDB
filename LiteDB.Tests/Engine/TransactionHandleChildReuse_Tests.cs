#if !NETFRAMEWORK
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using LiteDB.Internals;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleChildReuse_Tests
    {
        internal static SharedEngine Cached(SharedEngine shared) => (SharedEngine)typeof(SharedEngine)
            .GetField("_cachedTransactionChild", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(shared);

        internal static LiteEngine Core(ILiteTransaction tx) => ((TransactionResources)typeof(LiteTransaction)
            .GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tx)).Engine;

        private static void Idle(Thread thread) => Assert.True(SpinWait.SpinUntil(
            () => SharedHolderScheduler.IsIdle(thread), TimeSpan.FromSeconds(5)));

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public async Task Reused_holder_and_wrapper_release_writer_and_refresh_after_external_commit(string password)
        {
            using var file = new TempFile();
            await MvccProcess.Run("seed", file, password);
            Thread worker = null;
            TransactionAdmission.Observe = stage => { if (stage == "storage-opened") worker = Thread.CurrentThread; };
            // The process handshake can take longer than the production idle expiry.
            // Keep the holder alive so this test proves reuse across external writes.
            SharedHolderScheduler.IdleWaitOverride = 60000;
            try
            {
                using (var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password }))
                using (var db = new LiteDatabase(shared, disposeOnClose: false))
                {
                    LiteEngine previousCore;
                    using (var tx = db.BeginTransaction())
                    {
                        previousCore = Core(tx);
                        tx.GetCollection("docs").EnsureIndex("value");
                        Verify(tx, 0);
                        tx.Commit();
                    }
                    var cached = Cached(shared);
                    Assert.NotNull(cached);
                    var originalWorker = worker;
                    Idle(originalWorker);
                    for (var value = 1; value <= 3; value++)
                    {
                        Assert.Null(typeof(SharedEngine).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cached));
                        // Successful completion proves native ownership is released even
                        // while the exact same wrapper and holder thread remain alive.
                        await MvccProcess.Run("write", file, password, value.ToString());
                        using var tx = db.BeginTransaction(TimeSpan.FromSeconds(5));
                        Assert.Same(originalWorker, worker);
                        Assert.NotSame(previousCore, Core(tx));
                        previousCore = Core(tx);
                        Verify(tx, value);
                        // A rollback through the reused wrapper must not leak to the
                        // next process or the next newly opened core.
                        tx.GetCollection("docs").Update(new BsonDocument { ["_id"] = 0, ["value"] = 99 });
                        tx.Rollback();
                        Assert.Same(cached, Cached(shared));
                        Idle(originalWorker);
                    }
                    db.Dispose();
                    shared.Dispose();
                    Assert.Null(Cached(shared));
                }
            }
            finally
            {
                TransactionAdmission.Observe = null;
                SharedHolderScheduler.IdleWaitOverride = null;
            }
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                VerifyRows(cold.GetCollection("docs"), 3);
                VerifyRows(cold.GetCollection("cold"), 0, indexed: false);
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("secret", false)]
        [InlineData("secret", true)]
        public async Task Cached_wrapper_native_wait_timeout_or_cancellation_allows_retry(string password, bool cancel)
        {
            using var file = new TempFile();
            await MvccProcess.Run("seed", file, password);
            using var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password });
            using var db = new LiteDatabase(shared, disposeOnClose: false);
            using (var warm = db.BeginTransaction()) warm.Commit();
            var cached = Cached(shared);
            Assert.NotNull(cached);
            using var peer = new MvccProcess("uncommitted", file, password);
            await peer.Expect("ready");
            using var cancellation = new CancellationTokenSource();
            using var waiting = new ManualResetEventSlim();
            TransactionAdmission.Observe = stage => { if (stage == "native-wait") waiting.Set(); };
            try
            {
                var pending = Task.Run(() => Record.Exception(() =>
                {
                    using var tx = db.BeginTransaction(cancel ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(1), cancellation.Token);
                }));
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
                Assert.Null(Cached(shared)); // The waiting holder checked out the cached wrapper.
                if (cancel) cancellation.Cancel();
                var error = await pending.WaitAsync(TimeSpan.FromSeconds(5));
                if (cancel) Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(error).CancellationToken);
                else Assert.IsType<TimeoutException>(error);
                Assert.Null(Cached(shared)); // Failed acquisition cannot publish a reusable child.
            }
            finally { cancellation.Cancel(); TransactionAdmission.Observe = null; }
            await peer.Finish(release: true);
            using var retry = db.BeginTransaction(TimeSpan.FromSeconds(5));
            VerifyRows(retry.GetCollection("docs"), 0, indexed: false);
            retry.Commit();
            Assert.NotSame(cached, Cached(shared));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public async Task Handle_only_facade_preserves_direct_process_admission_between_handles(string password)
        {
            using var file = new TempFile();
            using (var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password }))
            using (var db = new LiteDatabase(shared, disposeOnClose: false))
            {
                using (var warm = db.BeginTransaction())
                {
                    warm.GetCollection("rows").EnsureIndex("value");
                    warm.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 42 });
                    warm.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                    warm.Commit();
                }
                var cached = Cached(shared);
                Assert.NotNull(cached);
                // Do not open the parent through an ordinary operation: that would
                // acquire its independent Shared lifetime admission and hide a leak.
                await MvccProcess.Run("native-write", file, password, "direct");
                using var next = db.BeginTransaction(TimeSpan.FromSeconds(5));
                Assert.Equal(84, next.GetCollection("rows").FindById(1)["value"].AsInt32);
                Assert.Single(next.GetCollection("rows").Find(Query.EQ("value", 84)));
                Assert.NotNull(next.GetCollection("sentinel").FindById(9));
                next.Commit();
                Assert.Same(cached, Cached(shared));
            }
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Assert.Equal(84, cold.GetCollection("rows").FindById(1)["value"].AsInt32);
                Assert.NotNull(cold.GetCollection("sentinel").FindById(9));
            }
        }

        private static void Verify(ILiteTransaction tx, int value)
        {
            VerifyRows(tx.GetCollection("docs"), value);
            VerifyRows(tx.GetCollection("cold"), 0, indexed: false);
        }

        private static void VerifyRows(ILiteCollection<BsonDocument> rows, int value, bool indexed = true)
        {
            var documents = rows.FindAll().OrderBy(row => row["_id"].AsInt32).ToArray();
            Assert.Equal(Enumerable.Range(0, 64), documents.Select(row => row["_id"].AsInt32));
            Assert.All(documents, row =>
            {
                Assert.Equal(value, row["value"].AsInt32);
                Assert.Equal(new string('x', 3000), row["payload"].AsString);
            });
            if (indexed)
            {
                var query = rows.Query().Where(Query.EQ("value", value));
                var index = query.GetPlan()["index"];
                Assert.Equal("value", index["name"].AsString);
                Assert.StartsWith("INDEX SEEK", index["mode"].AsString);
                Assert.Equal(Enumerable.Range(0, 64), query.ToArray().Select(row => row["_id"].AsInt32).OrderBy(id => id));
                Assert.Empty(rows.Find(Query.EQ("value", 99)));
            }
        }
    }
}
#endif
