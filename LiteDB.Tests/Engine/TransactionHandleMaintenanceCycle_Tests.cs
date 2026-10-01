using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using LiteDB.Engine;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleMaintenanceCycle_Tests
    {
        private static readonly ConcurrentBag<object> Retained = new ConcurrentBag<object>();
        private readonly ITestOutputHelper _output;
        public TransactionHandleMaintenanceCycle_Tests(ITestOutputHelper output) { _output = output; }

        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public void Rebuild_callback_dependency_ends_at_maintenance_deadline_and_preserves_state(string password)
        {
            var file = new TempFile();
            var callbackEntered = new ManualResetEventSlim();
            var startDependency = new ManualResetEventSlim();
            var freshBlocked = new ManualResetEventSlim();
            var allowWait = new ManualResetEventSlim();
            var freshDone = new ManualResetEventSlim();
            var history = new ConcurrentQueue<string>();
            var workers = new List<Thread>();
            Exception readError = null, freshError = null, rebuildError = null;
            LiteDatabase db = null;
            LiteEngine engine = null;
            var armed = false;
            Exception failure = null;
            var verified = false;
            try
            {
                engine = new LiteEngine(new EngineSettings
                {
                    Filename = file,
                    Password = password,
                    ReadTransform = (collection, value) =>
                    {
                        if (collection != "rows" || !armed) return value;
                        history.Enqueue("callback-entered");
                        callbackEntered.Set();
                        Assert.True(startDependency.Wait(TimeSpan.FromSeconds(10)));
                        var fresh = new Thread(() =>
                        {
                            try
                            {
                                history.Enqueue("fresh-invoked");
                                Assert.Equal(1, db.GetCollection("sentinel").Count());
                                history.Enqueue("fresh-completed");
                            }
                            catch (Exception error) { freshError = error; }
                            finally { freshDone.Set(); }
                        }) { IsBackground = true };
                        lock (workers) workers.Add(fresh);
                        fresh.Start();
                        Assert.True(freshDone.Wait(TimeSpan.FromSeconds(15)), "Fresh worker did not finish after the maintenance deadline.");
                        history.Enqueue("callback-returned");
                        return value;
                    }
                });
                db = new LiteDatabase(engine, disposeOnClose: false);
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
                db.GetCollection("rows").EnsureIndex("value");
                db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9, ["value"] = 90 });
                // A new fixture's explicit maintenance budget; no existing test timeout changes.
                db.Timeout = TimeSpan.FromSeconds(2);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var operations = (OperationLifetime)typeof(LiteEngine).GetField("_operations", flags).GetValue(engine);
                var pending = typeof(OperationLifetime).GetField("_waitingExclusive", flags);
                operations.WaitingForMaintenance = () =>
                {
                    history.Enqueue("fresh-at-maintenance-fence");
                    freshBlocked.Set();
                    Assert.True(allowWait.Wait(TimeSpan.FromSeconds(10)));
                };
                armed = true;
                var read = new Thread(() =>
                {
                    try { Assert.NotNull(db.GetCollection("rows").FindById(1)); }
                    catch (Exception error) { readError = error; }
                }) { IsBackground = true };
                workers.Add(read);
                read.Start();
                Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(10)));
                var rebuild = new Thread(() =>
                {
                    history.Enqueue("rebuild-invoked");
                    rebuildError = Record.Exception(() => engine.Rebuild());
                    history.Enqueue("rebuild-returned");
                }) { IsBackground = true };
                workers.Add(rebuild);
                rebuild.Start();
                Assert.True(SpinWait.SpinUntil(() => (int)pending.GetValue(operations) == 1,
                    TimeSpan.FromSeconds(10)), "Rebuild did not publish its maintenance reservation.");
                history.Enqueue("maintenance-reserved");
                startDependency.Set();
                Assert.True(freshBlocked.Wait(TimeSpan.FromSeconds(10)), "The fresh actor must reach the actual maintenance admission boundary.");
                Assert.False(freshDone.IsSet);
                Assert.True(read.IsAlive);
                allowWait.Set();
                Assert.True(rebuild.Join(TimeSpan.FromSeconds(10)));
                var timeout = Assert.IsType<LiteException>(rebuildError);
                Assert.Equal(LiteException.LOCK_TIMEOUT, timeout.ErrorCode);
                Assert.Contains("operation/maintenance", timeout.Message);
                Assert.True(read.Join(TimeSpan.FromSeconds(10)));
                Assert.True(freshDone.Wait(TimeSpan.FromSeconds(10)));
                Thread[] all;
                lock (workers) all = workers.ToArray();
                foreach (var worker in all) Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
                Assert.Null(readError);
                Assert.Null(freshError);
                Assert.Equal(0, (int)pending.GetValue(operations));
                armed = false;
                operations.WaitingForMaintenance = null;
                engine.Rebuild(); // The failed reservation must not poison a later attempt.
                db.Dispose(); db = null;
                engine.Dispose(); engine = null;
                for (var reopen = 0; reopen < 2; reopen++)
                {
                    using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                    var query = cold.GetCollection("rows").Query().Where(Query.EQ("value", 10));
                    Assert.Equal("value", query.GetPlan()["index"]["name"].AsString);
                    Assert.Equal(new[] { 1 }, query.ToArray().Select(row => row["_id"].AsInt32));
                    Assert.Equal(1, cold.GetCollection("rows").Count());
                    Assert.Equal(90, cold.GetCollection("sentinel").FindById(9)["value"].AsInt32);
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
                startDependency.Set();
                allowWait.Set();
                Thread[] all;
                lock (workers) all = workers.ToArray();
                foreach (var worker in all) if (worker.IsAlive) worker.Join(TimeSpan.FromSeconds(2));
                _output.WriteLine("Schedule C06/1; fixture=" + file.Filename + "; " + string.Join(" -> ", history));
                if (all.Any(worker => worker.IsAlive))
                {
                    Retained.Add(new object[] { file, db, engine, workers, callbackEntered, startDependency, freshBlocked, allowWait, freshDone });
                    _output.WriteLine("Live worker: original fixture and owner graph retained; cold reopen not attempted.");
                }
                else
                {
                    Cleanup(() => db?.Dispose());
                    Cleanup(() => engine?.Dispose());
                    Cleanup(callbackEntered.Dispose); Cleanup(startDependency.Dispose); Cleanup(freshBlocked.Dispose);
                    Cleanup(allowWait.Dispose); Cleanup(freshDone.Dispose);
                    if (failure == null && verified && cleanupErrors.Count == 0) Cleanup(file.Dispose);
                    else
                    {
                        GC.SuppressFinalize(file); // TempFile's finalizer also deletes the original fixture.
                        _output.WriteLine("Failed scenario: original fixture retained at " + file.Filename +
                            "; cold verification completed=" + verified + ". Cleanup errors may leave admission unavailable; no reopen is attempted while ownership is uncertain.");
                    }
                }
                if (cleanupErrors.Count != 0)
                {
                    var cleanup = new AggregateException("Concurrency scenario cleanup failed; fixture retained at " + file.Filename, cleanupErrors);
                    _output.WriteLine(cleanup.ToString());
                    if (failure != null) failure.Data["LiteDB.AuditCleanup"] = cleanup;
                    else throw cleanup;
                }
            }
        }
    }
}
