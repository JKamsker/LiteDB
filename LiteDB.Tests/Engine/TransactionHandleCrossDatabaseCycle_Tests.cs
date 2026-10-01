using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleCrossDatabaseCycle_Tests
    {
        private static readonly ConcurrentBag<object> Retained = new ConcurrentBag<object>();
        private readonly ITestOutputHelper _output;
        public TransactionHandleCrossDatabaseCycle_Tests(ITestOutputHelper output) { _output = output; }

        public sealed class CallbackRow
        {
            [BsonIgnore] public Action Callback;
            public int Id { get; set; }
            public int Value { get { Callback?.Invoke(); return Id * 10; } set { } }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public void Opposed_database_callback_admission_is_cancellable_without_partial_transactions(string password)
        {
            var files = new[] { new TempFile(), new TempFile() };
            var databases = new LiteDatabase[2];
            var handles = new ILiteTransaction[2];
            var workers = new Thread[2];
            var cancellations = new[] { new CancellationTokenSource(), new CancellationTokenSource() };
            var waiting = new[] { new ManualResetEventSlim(), new ManualResetEventSlim() };
            var finished = new[] { new ManualResetEventSlim(), new ManualResetEventSlim() };
            var start = new ManualResetEventSlim();
            var history = new ConcurrentQueue<string>();
            var errors = new Exception[2];
            var callbackCounts = new int[2];
            var refused = new int[2];
            var originalObserver = TransactionAdmission.Observe;
            Exception failure = null;
            var verified = false;
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    databases[i] = new LiteDatabase(Settings(files[i], password));
                    databases[i].GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["Value"] = 10 });
                    databases[i].GetCollection("rows").EnsureIndex("Value");
                    databases[i].GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9, ["value"] = 90 + i });
                    handles[i] = databases[i].BeginTransaction();
                }
                TransactionAdmission.Observe = stage =>
                {
                    if (stage != "local-wait") return;
                    for (var actor = 0; actor < 2; actor++)
                        if (ReferenceEquals(Thread.CurrentThread, workers[actor]))
                        {
                            history.Enqueue(actor + ":local-admission-boundary");
                            waiting[actor].Set();
                        }
                };
                for (var i = 0; i < 2; i++)
                {
                    var actor = i;
                    workers[i] = new Thread(() =>
                    {
                        try
                        {
                            Assert.True(start.Wait(TimeSpan.FromSeconds(10)));
                            handles[actor].GetCollection<CallbackRow>("rows").Insert(new CallbackRow
                            {
                                Id = 2,
                                Callback = () =>
                                {
                                    Interlocked.Increment(ref callbackCounts[actor]);
                                    history.Enqueue(actor + ":callback-entered");
                                    var error = Assert.Throws<OperationCanceledException>(() =>
                                        databases[1 - actor].BeginTransaction(Timeout.InfiniteTimeSpan, cancellations[actor].Token));
                                    Assert.Equal(cancellations[actor].Token, error.CancellationToken);
                                    Interlocked.Increment(ref refused[actor]);
                                    history.Enqueue(actor + ":admission-cancelled");
                                }
                            });
                            history.Enqueue(actor + ":write-completed");
                        }
                        catch (Exception error) { errors[actor] = error; }
                        finally { finished[actor].Set(); }
                    }) { IsBackground = true };
                    workers[i].Start();
                }
                start.Set();
                Assert.True(waiting[0].Wait(TimeSpan.FromSeconds(10)));
                Assert.True(waiting[1].Wait(TimeSpan.FromSeconds(10)));
                // Both destination gates are occupied by still-active source handles.
                // No scheduler timeout or peer's progress counts as cancellation success.
                Assert.Equal(LiteTransactionState.Active, handles[0].State);
                Assert.Equal(LiteTransactionState.Active, handles[1].State);
                Assert.False(finished[0].IsSet);
                Assert.False(finished[1].IsSet);
                history.Enqueue("controller:cancel-both");
                cancellations[0].Cancel();
                cancellations[1].Cancel();
                for (var i = 0; i < 2; i++)
                {
                    Assert.True(finished[i].Wait(TimeSpan.FromSeconds(10)), "Individual actor " + i + " did not finish.");
                    Assert.True(workers[i].Join(TimeSpan.FromSeconds(5)));
                    Assert.Null(errors[i]);
                    Assert.Equal(1, callbackCounts[i]);
                    Assert.Equal(1, refused[i]);
                    Assert.Equal(LiteTransactionState.Active, handles[i].State);
                }
                handles[0].Commit();
                handles[1].Rollback();
                for (var i = 0; i < 2; i++) { handles[i].Dispose(); handles[i] = null; }

                // Positive control: only A has an owner, so A -> B must be accepted.
                using (var outer = databases[0].BeginTransaction())
                {
                    var entered = false;
                    outer.GetCollection<CallbackRow>("rows").Insert(new CallbackRow
                    {
                        Id = 4,
                        Callback = () =>
                        {
                            using var independent = databases[1].BeginTransaction(TimeSpan.FromSeconds(5));
                            independent.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 3, ["Value"] = 30 });
                            independent.Commit();
                            entered = true;
                        }
                    });
                    Assert.True(entered);
                    outer.Rollback();
                }
                for (var i = 0; i < 2; i++) { databases[i].Dispose(); databases[i] = null; }
                for (var reopen = 0; reopen < 2; reopen++)
                    for (var i = 0; i < 2; i++)
                    {
                        using var cold = new LiteDatabase(Settings(files[i], password));
                        var expected = i == 0 ? new[] { 1, 2 } : new[] { 1, 3 };
                        var query = cold.GetCollection("rows").Query().Where(Query.GTE("Value", 10));
                        Assert.Equal("Value", query.GetPlan()["index"]["name"].AsString);
                        Assert.Equal(expected, query.ToArray().Select(row => row["_id"].AsInt32).OrderBy(id => id));
                        Assert.Equal(expected.Length, cold.GetCollection("rows").Count());
                        Assert.Equal(90 + i, cold.GetCollection("sentinel").FindById(9)["value"].AsInt32);
                    }
                verified = true;
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                var cleanupErrors = new List<Exception>();
                void Cleanup(Action action)
                {
                    try { action(); }
                    catch (Exception error) { cleanupErrors.Add(error); }
                }
                start.Set();
                foreach (var cancellation in cancellations) Cleanup(cancellation.Cancel);
                foreach (var worker in workers) if (worker?.IsAlive == true) worker.Join(TimeSpan.FromSeconds(2));
                TransactionAdmission.Observe = originalObserver;
                _output.WriteLine("Schedule C22/1; fixtures=" + string.Join(",", files.Select(file => file.Filename)) + "; " + string.Join(" -> ", history));
                if (workers.Any(worker => worker?.IsAlive == true))
                {
                    Retained.Add(new object[] { files, databases, handles, workers, cancellations, waiting, finished, start });
                    _output.WriteLine("Live actor: original fixtures and owner graphs retained; cold reopen not attempted.");
                }
                else
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var actor = i;
                        Cleanup(() => handles[actor]?.Dispose());
                        Cleanup(() => databases[actor]?.Dispose());
                        Cleanup(cancellations[actor].Dispose); Cleanup(waiting[actor].Dispose); Cleanup(finished[actor].Dispose);
                    }
                    Cleanup(start.Dispose);
                    if (failure == null && verified && cleanupErrors.Count == 0)
                        foreach (var file in files) Cleanup(file.Dispose);
                    else
                    {
                        foreach (var file in files) GC.SuppressFinalize(file); // TempFile finalization deletes files too.
                        _output.WriteLine("Failed scenario: original fixtures retained; cold verification completed=" + verified +
                            ". Cleanup errors may leave admission unavailable; no reopen is attempted while ownership is uncertain.");
                    }
                }
                if (cleanupErrors.Count != 0)
                {
                    var cleanup = new AggregateException("Concurrency scenario cleanup failed; original fixtures retained.", cleanupErrors);
                    _output.WriteLine(cleanup.ToString());
                    if (failure != null) failure.Data["LiteDB.AuditCleanup"] = cleanup;
                    else throw cleanup;
                }
            }
        }

        private static ConnectionString Settings(TempFile file, string password) =>
            new ConnectionString { Filename = file, Password = password, Connection = ConnectionType.Shared };
    }
}
