using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleAdmissionLifetime_Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Caller_cancellation_and_session_close_drain_pending_begin_without_touching_peer(bool cancelFirst)
        {
            using var file = new TempFile();
            using var ownerDb = TransactionHandleAdmission_Tests.Open(file);
            using var waiter = TransactionHandleAdmission_Tests.Open(file);
            using var owner = ownerDb.BeginTransaction();
            owner.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var cancellation = new CancellationTokenSource();
            using var waiting = new ManualResetEventSlim();
            TransactionAdmission.Observe = stage => { if (stage == "local-wait") waiting.Set(); };
            try
            {
                var pending = Task.Run(() => Record.Exception(() =>
                { using var tx = waiter.BeginTransaction(Timeout.InfiniteTimeSpan, cancellation.Token); }));
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
                if (cancelFirst) cancellation.Cancel();
                waiter.Dispose();
                cancellation.Cancel();
                Assert.IsAssignableFrom<OperationCanceledException>(await pending);
                Assert.Equal(LiteTransactionState.Active, owner.State);
                owner.Commit();
            }
            finally { cancellation.Cancel(); TransactionAdmission.Observe = null; }
            Assert.NotNull(ownerDb.GetCollection("rows").FindById(1));
        }

        [Fact]
        public void Admission_cancellation_remains_primary_when_stream_cleanup_also_fails()
        {
            using var file = new TempFile();
            using var db = TransactionHandleAdmission_Tests.Open(file);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
            using var cancellation = new CancellationTokenSource();
            var fail = 1;
            var reached = false;
            var cleanup = new IOException("admission cleanup failure");
            NativeAdmissionStreamProbe.Attach = (path, writable) => stage =>
            {
                if (stage == "disposed" && Interlocked.Exchange(ref fail, 0) == 1) throw cleanup;
            };
            TransactionAdmission.Observe = stage =>
            { if (stage == "storage-opened") { reached = true; cancellation.Cancel(); } };
            try
            {
                var error = Assert.Throws<OperationCanceledException>(() =>
                    db.BeginTransaction(Timeout.InfiniteTimeSpan, cancellation.Token));
                Assert.Equal(cancellation.Token, error.CancellationToken);
                Assert.True(reached);
                Assert.Contains("admission cleanup failure", string.Join(" ", error.Data.Values.Cast<object>()));
            }
            finally { NativeAdmissionStreamProbe.Attach = null; TransactionAdmission.Observe = null; }
            using var retry = db.BeginTransaction(TimeSpan.Zero);
            Assert.NotNull(retry.GetCollection("sentinel").FindById(1));
            retry.Commit();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Admission_does_not_root_abandoned_handle_through_callback_or_execution_context(bool asyncLocal)
        {
            using var file = new TempFile();
            WeakReference[] references = null;
            TransactionHandle_Tests.OnThread(() => references = Abandon(file, asyncLocal));
            for (var attempt = 0; attempt < 100 &&
                (references.Any(reference => reference.IsAlive) || NativeAdmissionDirectPool_Tests.Locked(file)); attempt++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(20);
            }
            Assert.Equal(new[] { false, false, false }, references.Select(reference => reference.IsAlive));
            Assert.False(NativeAdmissionDirectPool_Tests.Locked(file));
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("sentinel").FindById(1));
            Assert.Null(cold.GetCollection("rows").FindById(2));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] Abandon(string file, bool asyncLocal)
        {
            var db = TransactionHandleAdmission_Tests.Open(file);
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
            var cancellation = new CancellationTokenSource();
            // Only an admission resource retained by the holder could externally root this cycle.
            cancellation.Token.Register(() => GC.KeepAlive(db));
            var ambient = new AsyncLocal<LiteDatabase>();
            if (asyncLocal) ambient.Value = db;
            var tx = db.BeginTransaction(Timeout.InfiniteTimeSpan, cancellation.Token);
            tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["payload"] = new string('x', 50000) });
            return new[] { new WeakReference(db), new WeakReference(tx), new WeakReference(cancellation) };
        }
    }
}
