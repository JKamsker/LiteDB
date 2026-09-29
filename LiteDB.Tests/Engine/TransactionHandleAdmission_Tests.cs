using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleAdmission_Tests
    {
        internal static LiteDatabase Open(string file) => new LiteDatabase(new ConnectionString
            { Filename = file, Connection = ConnectionType.Shared });

        [Fact]
        public void Invalid_and_precanceled_options_do_not_admit_and_zero_attempts_without_waiting()
        {
            using var file = new TempFile();
            using var db = Open(file);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<ArgumentOutOfRangeException>(() => db.BeginTransaction(TimeSpan.FromMilliseconds(-2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => db.BeginTransaction(TimeSpan.FromMilliseconds((long)int.MaxValue + 1)));
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() =>
                db.BeginTransaction(Timeout.InfiniteTimeSpan, cancellation.Token)).CancellationToken);
            using var first = db.BeginTransaction(TimeSpan.Zero);
            first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            Assert.Throws<TimeoutException>(() => db.BeginTransaction(TimeSpan.Zero));
            Assert.Throws<TimeoutException>(() => db.BeginTransaction(TimeSpan.FromMilliseconds(30)));
            Assert.Equal(LiteTransactionState.Active, first.State);
            first.Commit();
            using var next = db.BeginTransaction(TimeSpan.Zero);
            Assert.NotNull(next.GetCollection("rows").FindById(1));
            next.Rollback();
        }

        [Fact]
        public async Task Local_wait_cancellation_preserves_owner_and_allows_retry()
        {
            using var file = new TempFile();
            using var db = Open(file);
            using var owner = db.BeginTransaction();
            owner.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var cancellation = new CancellationTokenSource();
            using var waiting = new ManualResetEventSlim();
            TransactionAdmission.Observe = stage => { if (stage == "local-wait") waiting.Set(); };
            try
            {
                var pending = Task.Run(() => Assert.Throws<OperationCanceledException>(() =>
                    db.BeginTransaction(Timeout.InfiniteTimeSpan, cancellation.Token)));
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, (await pending).CancellationToken);
                Assert.Equal(LiteTransactionState.Active, owner.State);
                owner.Commit();
            }
            finally { cancellation.Cancel(); TransactionAdmission.Observe = null; }
            using var next = db.BeginTransaction(TimeSpan.Zero);
            Assert.NotNull(next.GetCollection("rows").FindById(1));
            next.Commit();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Native_wait_timeout_or_cancellation_preserves_independent_legacy_owner(bool cancel)
        {
            using var file = new TempFile();
            using var owner = Open(file);
            using var waiter = Open(file);
            owner.BeginTrans();
            owner.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var cancellation = new CancellationTokenSource();
            using var waiting = new ManualResetEventSlim();
            TransactionAdmission.Observe = stage => { if (stage == "native-wait") waiting.Set(); };
            try
            {
                var pending = Task.Run(() => Record.Exception(() =>
                { using var tx = waiter.BeginTransaction(cancel ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(100), cancellation.Token); }));
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
                if (cancel) cancellation.Cancel();
                Assert.True(pending.Wait(TimeSpan.FromSeconds(5)));
                if (cancel) Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(pending.Result).CancellationToken);
                else Assert.IsType<TimeoutException>(pending.Result);
                Assert.True(owner.Commit()); // Legacy completion stays on its original thread.
            }
            finally { cancellation.Cancel(); TransactionAdmission.Observe = null; owner.Rollback(); }
            // Legacy completion posts native release to its existing holder. The
            // peer remains live; allow that handoff instead of assuming zero-delay release.
            using var next = waiter.BeginTransaction(TimeSpan.FromSeconds(5));
            Assert.NotNull(next.GetCollection("rows").FindById(1));
            next.Commit();
        }

        [Fact]
        public void Both_shared_waits_spend_one_deadline()
        {
            using var file = new TempFile();
            using var db = Open(file);
            using var legacy = Open(file);
            using var first = db.BeginTransaction();
            using var local = new ManualResetEventSlim();
            using var acquired = new ManualResetEventSlim();
            using var continueNative = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource();
            long now = 0;
            TransactionAdmission.TimestampOverride = () => Interlocked.Read(ref now);
            TransactionAdmission.Observe = stage =>
            {
                if (stage == "local-wait") local.Set();
                if (stage == "local-acquired")
                {
                    acquired.Set();
                    Assert.True(continueNative.Wait(TimeSpan.FromSeconds(5)));
                }
                if (stage == "native-wait") Interlocked.Exchange(ref now, Stopwatch.Frequency * 101 / 100);
            };
            Task<Exception> pending = null;
            try
            {
                pending = Task.Run(() => Record.Exception(() =>
                { using var tx = db.BeginTransaction(TimeSpan.FromSeconds(1), cancellation.Token); }));
                Assert.True(local.Wait(TimeSpan.FromSeconds(5)));
                Interlocked.Exchange(ref now, Stopwatch.Frequency * 3 / 4);
                first.Rollback();
                Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
                legacy.BeginTrans();
                continueNative.Set();
                Assert.True(pending.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsType<TimeoutException>(pending.Result);
            }
            finally
            {
                cancellation.Cancel(); continueNative.Set(); legacy.Rollback();
                pending?.Wait(TimeSpan.FromSeconds(5));
                TransactionAdmission.Observe = null;
                TransactionAdmission.TimestampOverride = null;
            }
            // The legacy owner above releases asynchronously, unlike a completed handle.
            using var retry = db.BeginTransaction(TimeSpan.FromSeconds(5));
            retry.Commit();
        }

        [Theory]
        [InlineData("local-acquired")]
        [InlineData("native-acquired")]
        [InlineData("storage-opened")]
        public void Cancellation_after_acquisition_releases_ownership_before_reporting_failure(string at)
        {
            using var file = new TempFile();
            using var db = Open(file);
            using var cancellation = new CancellationTokenSource();
            var reached = false;
            TransactionAdmission.Observe = stage => { if (stage == at) { reached = true; cancellation.Cancel(); } };
            try
            {
                Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() =>
                    db.BeginTransaction(Timeout.InfiniteTimeSpan, cancellation.Token)).CancellationToken);
                Assert.True(reached);
            }
            finally { TransactionAdmission.Observe = null; }
            using var retry = db.BeginTransaction(TimeSpan.Zero);
            retry.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            retry.Commit();
            db.Dispose();
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("rows").FindById(1));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Cancellation_after_return_does_not_cancel_transaction_or_commit(bool shared)
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(new ConnectionString { Filename = file,
                Connection = shared ? ConnectionType.Shared : ConnectionType.Direct });
            using var cancellation = new CancellationTokenSource();
            using var tx = ((ILiteDatabase)db).BeginTransaction(TimeSpan.Zero, cancellation.Token);
            cancellation.Cancel();
            tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            TransactionHandle_Tests.OnThread(tx.Commit);
            db.Dispose();
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("rows").FindById(1));
        }
    }
}
