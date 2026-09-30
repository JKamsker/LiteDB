using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleRawCloseDependency_Tests
    {
        [Fact]
        public async Task Close_wakes_fresh_work_already_waiting_behind_rebuild()
        {
            var operations = new OperationLifetime();
            using var waiting = new ManualResetEventSlim();
            operations.WaitingForMaintenance = waiting.Set;
            var active = operations.Enter();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var pending = typeof(OperationLifetime).GetField("_waitingExclusive", flags);
            var rebuild = Task.Run(() =>
            { using (operations.Exclusive(() => true, TimeSpan.FromSeconds(10))) { } });
            Task close = null;
            try
            {
                Assert.True(SpinWait.SpinUntil(() => (int)pending.GetValue(operations) == 1,
                    TimeSpan.FromSeconds(5)));
                var fresh = Task.Run(() => Record.Exception(() =>
                { using (operations.Enter()) { } }));
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
                close = Task.Run(() =>
                { using (operations.Exclusive(() => true, closing: true)) { } });
                Assert.True(fresh.Wait(TimeSpan.FromSeconds(5)), "Close must wake previously queued independent work.");
                Assert.Equal(LiteException.ENGINE_DISPOSED, Assert.IsType<LiteException>(await fresh).ErrorCode);
                Assert.False(close.IsCompleted);
            }
            finally { active.Dispose(); }
            await rebuild;
            if (close != null) await close;
        }

        [Fact]
        public async Task Raw_close_rejects_fresh_callback_dependency_before_draining_callback()
        {
            using var file = new TempFile();
            using var callbackEntered = new ManualResetEventSlim();
            using var startDependency = new ManualResetEventSlim();
            LiteDatabase db = null;
            Task<Exception> dependency = null;
            var dependencyFinished = false;
            using var engine = new LiteEngine(new EngineSettings
            {
                Filename = file,
                ReadTransform = (collection, value) =>
                {
                    if (collection != "rows") return value;
                    callbackEntered.Set();
                    Assert.True(startDependency.Wait(TimeSpan.FromSeconds(10)));
                    dependency = Task.Factory.StartNew(() => Record.Exception(() =>
                        db.GetCollection("untouched").Count()), CancellationToken.None,
                        TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    dependencyFinished = dependency.Wait(TimeSpan.FromSeconds(2));
                    return value;
                }
            });
            using (db = new LiteDatabase(engine, disposeOnClose: false))
            {
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
                db.GetCollection("rows").EnsureIndex("value");
                db.GetCollection("untouched").Insert(new BsonDocument { ["_id"] = 42 });
                var read = Task.Factory.StartNew(() => Record.Exception(() =>
                    db.GetCollection("rows").FindById(1)), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(10)));
                var close = Task.Factory.StartNew(engine.Dispose, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var operations = typeof(LiteEngine).GetField("_operations", flags).GetValue(engine);
                    Assert.True(SpinWait.SpinUntil(() =>
                        (int)operations.GetType().GetField("_waitingExclusive", flags).GetValue(operations) != 0,
                        TimeSpan.FromSeconds(10)));
                }
                finally { startDependency.Set(); }
                var readError = await read;
                if (readError != null)
                    Assert.Equal(LiteException.ENGINE_DISPOSED, Assert.IsType<LiteException>(readError).ErrorCode);
                await close;
                var error = Assert.IsType<LiteException>(await dependency);
                Assert.Equal(LiteException.ENGINE_DISPOSED, error.ErrorCode);
                Assert.True(dependencyFinished, "Close must reject fresh work before waiting for an active callback that needs it.");
            }
            using var cold = new LiteDatabase(file);
            Assert.Equal(1, cold.GetCollection("rows").Count(Query.EQ("value", 10)));
            Assert.NotNull(cold.GetCollection("untouched").FindById(42));
        }
    }
}
