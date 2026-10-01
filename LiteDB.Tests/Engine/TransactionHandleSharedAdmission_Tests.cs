using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleSharedAdmission_Tests
    {
        private static SharedEngine Engine(LiteDatabase db) =>
            (SharedEngine)typeof(LiteDatabase).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(db);

#pragma warning disable CS0618
        [Theory]
        [InlineData(null, false)]
        [InlineData("secret", false)]
        [InlineData(null, true)]
        [InlineData("secret", true)]
        public void Closing_cancels_waiting_shared_call_before_draining_legacy_owner(string password, bool pinned)
        {
            using var file = new TempFile();
            var connection = new ConnectionString { Filename = file.Filename, Connection = ConnectionType.Shared, Password = password };
            var db = new LiteDatabase(connection);
            var rows = db.GetCollection("rows");
            rows.EnsureIndex("value");
            rows.Insert(Enumerable.Range(0, 150).Select(i => new BsonDocument { ["_id"] = i, ["value"] = i }));
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 7, ["value"] = "untouched" });
            IBsonDataReader reader = null;
            if (pinned)
            {
                reader = db.Execute("SELECT $ FROM rows");
                Assert.True(reader.Read());
                rows.Insert(new BsonDocument { ["_id"] = 200, ["value"] = 200 });
            }
            Assert.True(db.BeginTrans());
            rows.Insert(new BsonDocument { ["_id"] = 201, ["value"] = 201 });
            var engine = Engine(db);
            var waiters = typeof(SharedEngine).GetField("_mutexWaiters", BindingFlags.Instance | BindingFlags.NonPublic);
            var peer = Task.Factory.StartNew(() => Record.Exception(() => rows.Insert(
                new BsonDocument { ["_id"] = 202, ["value"] = 202 })), TaskCreationOptions.LongRunning);
            try
            {
                Assert.True(SpinWait.SpinUntil(() => (int)waiters.GetValue(engine) > 0, TimeSpan.FromSeconds(5)));
                Assert.False(peer.IsCompleted);
                db.Dispose();
                Assert.True(peer.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsType<OperationCanceledException>(peer.Result);
            }
            finally
            {
                engine.Dispose(); // Also frees the known-bad implementation's blocked waiter.
                peer.Wait(TimeSpan.FromSeconds(5));
                reader?.Dispose();
                db.Dispose();
            }
            using var reopen = new LiteDatabase(connection);
            var expected = Enumerable.Range(0, 150).Concat(pinned ? new[] { 200 } : Array.Empty<int>()).ToArray();
            Assert.Equal(expected, reopen.GetCollection("rows").FindAll().Select(x => x["_id"].AsInt32).OrderBy(x => x));
            Assert.Equal(expected, reopen.GetCollection("rows").Find(Query.GTE("value", 0)).Select(x => x["value"].AsInt32).OrderBy(x => x));
            Assert.Equal("untouched", reopen.GetCollection("sentinel").FindById(7)["value"].AsString);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Closing_cancels_native_wait_without_releasing_another_facades_owner(bool legacyWaiter)
        {
            using var file = new TempFile();
            var connection = new ConnectionString { Filename = file.Filename, Connection = ConnectionType.Shared };
            using var owner = new LiteDatabase(connection);
            owner.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
            using var transaction = owner.BeginTransaction();
            transaction.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
            var waiting = new LiteDatabase(connection);
            var engine = Engine(waiting);
            var turnstile = (LiteDB.Client.Shared.SharedMutexTurnstile)typeof(SharedEngine)
                .GetField("_turnstile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
            using var reached = new ManualResetEventSlim();
            turnstile.BeforeMainWait = () => reached.Set();
            var call = Task.Factory.StartNew(() => Record.Exception(() =>
            {
                if (legacyWaiter) waiting.BeginTrans();
                else waiting.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3 });
            }), TaskCreationOptions.LongRunning);
            try
            {
                Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
                waiting.Dispose();
                Assert.True(call.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsType<OperationCanceledException>(call.Result);
                Assert.Equal(2, transaction.GetCollection("rows").Count());
                transaction.Commit();
            }
            finally
            {
                transaction.Dispose();
                waiting.Dispose();
            }
            using var reopen = new LiteDatabase(connection);
            Assert.Equal(new[] { 1, 2 }, reopen.GetCollection("rows").FindAll().Select(x => x["_id"].AsInt32).OrderBy(x => x));
        }

#pragma warning restore CS0618
    }
}
