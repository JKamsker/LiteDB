using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleCallbackHandoff_Tests
    {
        [Theory]
        [InlineData(false, null)]
        [InlineData(false, "secret")]
        [InlineData(true, null)]
        [InlineData(true, "secret")]
        public void Public_admission_handoff_preserves_current_callback_marker(bool shared, string password)
        {
            using var file = new TempFile();
            var settings = new ConnectionString { Filename = file, Password = password,
                Connection = shared ? ConnectionType.Shared : ConnectionType.Direct };
            using (var db = new LiteDatabase(settings))
            {
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
                db.GetCollection("rows").EnsureIndex("value");
                db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                using var tx = db.BeginTransaction();
                tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 20 });
                var handle = (LiteTransaction)tx;
                var context = (TransactionContext)typeof(LiteTransaction).GetField("_transaction",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tx);
                var errors = new ConcurrentQueue<string>();
                using var start = new ManualResetEventSlim();
                var workers = new Thread[4];
                for (var workerIndex = 0; workerIndex < workers.Length; workerIndex++)
                {
                    workers[workerIndex] = new Thread(() =>
                    {
                        start.Wait();
                        var deadline = Stopwatch.StartNew();
                        for (var completed = 0; completed < 1000;)
                        {
                            if (deadline.Elapsed > TimeSpan.FromSeconds(15)) { errors.Enqueue("handoff deadline"); return; }
                            try
                            {
                                handle.Run(() =>
                                {
                                    if (!ReferenceEquals(context.ExecutingThread, Thread.CurrentThread)) errors.Enqueue("before");
                                    Thread.Yield();
                                    if (!ReferenceEquals(context.ExecutingThread, Thread.CurrentThread)) errors.Enqueue("after");
                                    return true;
                                });
                                completed++;
                            }
                            catch (InvalidOperationException) { Thread.Yield(); }
                        }
                    }) { IsBackground = true };
                    workers[workerIndex].Start();
                }
                start.Set();
                foreach (var worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(20)));
                Assert.Empty(errors);
                Assert.Null(context.ExecutingThread);
                Assert.Equal(LiteTransactionState.Active, tx.State);
                tx.Commit();
            }
            for (var reopen = 0; reopen < 2; reopen++)
            {
                using var db = new LiteDatabase(settings);
                var query = db.GetCollection("rows").Query().Where(Query.GTE("value", 10));
                Assert.Equal("value", query.GetPlan()["index"]["name"].AsString);
                Assert.Equal(new[] { 1, 2 }, query.ToArray().Select(row => row["_id"].AsInt32).OrderBy(id => id));
                Assert.Equal(2, db.GetCollection("rows").Count());
                Assert.NotNull(db.GetCollection("sentinel").FindById(9));
            }
        }
    }
}
