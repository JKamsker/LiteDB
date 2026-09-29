using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleLifetime_Tests
    {
        private static SessionLifetime Lifetime(LiteDatabase db) => (SessionLifetime)typeof(LiteDatabase)
            .GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(db);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Completed_and_failed_handles_release_ownership_without_disposal(bool shared)
        {
            using var file = new TempFile();
            using var db = Open(file, shared);
            var retained = new List<ILiteTransaction>();
            for (var i = 0; i < 6; i++)
            {
                var tx = db.BeginTransaction();
                retained.Add(tx);
                tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = i });
                if (i % 3 == 0) tx.Commit();
                else if (i % 3 == 1) tx.Rollback();
                else
                {
                    Assert.ThrowsAny<Exception>(() => tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = i }));
                    Assert.Equal(LiteTransactionState.Failed, tx.State);
                }
                Assert.Equal(0, Lifetime(db).ActiveHandles);
                Assert.Equal(i / 3 + 1, db.GetCollection("rows").Count());
            }
            db.Dispose();
            using (var reopened = Open(file, shared)) Assert.Equal(2, reopened.GetCollection("rows").Count());
            foreach (var tx in retained) { tx.Dispose(); tx.Dispose(); }
            GC.KeepAlive(retained);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Close_timeout_retains_work_then_cleans_up_without_a_retry(bool shared)
        {
            using var file = new TempFile();
            var db = Open(file, shared);
            Lifetime(db).CloseWaitOverride = TimeSpan.FromMilliseconds(100);
            using var tx = db.BeginTransaction();
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            IEnumerable<BsonDocument> Input()
            {
                yield return new BsonDocument { ["_id"] = 1 };
                entered.Set();
                Assert.True(resume.Wait(TimeSpan.FromSeconds(20)));
                yield return new BsonDocument { ["_id"] = 2 };
            }
            var rows = tx.GetCollection("rows");
            var write = Task.Run(() => rows.Insert(Input()));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                Assert.Throws<TimeoutException>(db.Dispose);
                Assert.Throws<ObjectDisposedException>(() => db.BeginTransaction());
                Assert.Throws<ObjectDisposedException>(() => db.GetCollection("other").Count());
                Assert.Equal(LiteTransactionState.Active, tx.State);
            }
            finally { resume.Set(); }
            await write;
            Assert.Equal(LiteTransactionState.RolledBack, tx.State);
            Assert.Equal(0, Lifetime(db).ActiveHandles);
            // No second disposal triggers cleanup: completion released ownership itself.
            using (var reopened = Open(file, shared))
            {
                Assert.Equal(0, reopened.GetCollection("rows").Count());
                reopened.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 3 });
            }
            db.Dispose();
            db.Dispose();
        }

        [Fact]
        public async Task Closing_shared_session_cancels_pending_begins_and_settles_idle_owner()
        {
            using var file = new TempFile();
            using var db = Open(file, true);
            using var first = db.BeginTransaction();
            first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var started = new CountdownEvent(12);
            var pending = Enumerable.Range(0, 12).Select(_ => Task.Factory.StartNew(() =>
            {
                started.Signal();
                try { using var unexpected = db.BeginTransaction(); return false; }
                catch (OperationCanceledException) { return true; }
                catch (ObjectDisposedException) { return true; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            db.Dispose();
            Assert.All(await Task.WhenAll(pending), Assert.True);
            Assert.Equal(LiteTransactionState.RolledBack, first.State);
            using var reopened = Open(file, true);
            Assert.Equal(0, reopened.GetCollection("rows").Count());
            using var next = reopened.BeginTransaction();
            next.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
            next.Commit();
        }

        [Fact]
        public async Task Closing_pending_shared_begin_preserves_an_independent_legacy_owner()
        {
            using var file = new TempFile();
            using var owner = Open(file, true);
            using var waiter = Open(file, true);
            owner.BeginTrans();
            owner.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var started = new ManualResetEventSlim();
            var begin = Task.Factory.StartNew(() =>
            {
                started.Set();
                try { using var unexpected = waiter.BeginTransaction(); return false; }
                catch (OperationCanceledException) { return true; }
                catch (ObjectDisposedException) { return true; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            // Ensure native admission has an opportunity to block behind the independent owner.
            Assert.False(begin.Wait(100));
            waiter.Dispose();
            Assert.True(owner.Commit());
            Assert.True(await begin);
            Assert.Equal(1, owner.GetCollection("rows").Count());
        }

        private static LiteDatabase Open(string file, bool shared) => new LiteDatabase(new ConnectionString
        { Filename = file, Connection = shared ? ConnectionType.Shared : ConnectionType.Direct });
    }
}
