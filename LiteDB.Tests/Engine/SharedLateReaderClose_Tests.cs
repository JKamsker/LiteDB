using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class SharedLateReaderClose_Tests
    {
        private static object Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
        [Theory]
        [InlineData(null, false)]
        [InlineData("secret", false)]
        [InlineData(null, true)]
        [InlineData("secret", true)]
        public void Late_reader_self_close_preserves_unleased_protection_or_leased_independence(string password, bool leased)
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(new ConnectionString { Filename = file, Password = password }))
            {
                seed.GetCollection("rows").Insert(Enumerable.Range(1, 5).Select(i => new BsonDocument { ["_id"] = i, ["value"] = i }));
                seed.GetCollection("rows").EnsureIndex("value");
            }
            var obstruction = file.Filename + "-readers";
            if (!leased) File.WriteAllText(obstruction, "refuse registration");
            Action callback = null;
            using var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password, ReadOnly = true,
                ReadTransform = (_, value) => { callback?.Invoke(); return value; } });
            var reader = shared.Query("rows", new Query());
            Assert.True(reader.Read());
            Assert.Equal(1, reader.Current["_id"].AsInt32);
            Assert.Null(Field(shared, "_engine"));
            var entered = 0;
            Exception refusal = null;
            var acquired = false;
            callback = () =>
            {
                callback = null;
                entered++;
                refusal = Record.Exception(shared.Dispose);
                var probe = new Thread(() =>
                {
                    if (!shared.MutexOwner.Mutex.WaitOne(0)) return;
                    acquired = true;
                    shared.MutexOwner.Mutex.ReleaseMutex();
                }) { IsBackground = true };
                probe.Start();
                Assert.True(probe.Join(TimeSpan.FromSeconds(3)));
            };
            Assert.True(reader.Read());
            Assert.Equal(2, reader.Current["_id"].AsInt32);
            var count = 2;
            while (reader.Read()) count++;
            reader.Dispose();
            shared.Dispose();
            if (!leased) File.Delete(obstruction);
            Assert.Equal(1, entered);
            Assert.Equal(5, count);
            if (leased) { Assert.Null(refusal); Assert.True(acquired); }
            else { Assert.IsType<InvalidOperationException>(refusal); Assert.False(acquired); }
            using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
            Assert.Equal(Enumerable.Range(1, 5), cold.GetCollection("rows").Find(Query.GTE("value", 0)).Select(x => x["_id"].AsInt32));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("secret", false)]
        [InlineData(null, true)]
        [InlineData("secret", true)]
        public void Late_reader_callback_can_reenter_after_foreign_close_starts_draining(string password, bool snapshot)
        {
            var file = new TempFile();
            using (var seed = new LiteDatabase(new ConnectionString { Filename = file, Password = password }))
                seed.GetCollection("rows").Insert(Enumerable.Range(1, 5).Select(i => new BsonDocument { ["_id"] = i }));
            var obstruction = file.Filename + "-readers";
            if (snapshot) File.WriteAllText(obstruction, "refuse registration");
            var reached = new ManualResetEventSlim();
            var resume = new ManualResetEventSlim();
            var readerDone = new ManualResetEventSlim();
            var closeDone = new ManualResetEventSlim();
            Action callback = null;
            var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password, ReadOnly = snapshot, ReadTransform = (_, value) => { callback?.Invoke(); return value; } });
            Exception readError = null, closeError = null, reentryError = null;
            LiteEngine core = null;
            var readerThread = new Thread(() =>
            {
                try
                {
                    if (!snapshot) shared.BeginTrans();
                    using var reader = shared.Query("rows", new Query());
                    Assert.True(reader.Read());
                    core = snapshot ? ((System.Collections.Generic.HashSet<LiteEngine>)Field(shared, "_mutexSnapshots")).Single()
                        : (LiteEngine)Field(shared, "_engine");
                    callback = () => { callback = null; reached.Set(); resume.Wait(); reentryError = Record.Exception(() => shared.Pragma("USER_VERSION")); };
                    reader.Read();
                }
                catch (Exception error) { readError = error; }
                finally { readerDone.Set(); }
            }) { IsBackground = true };
            readerThread.Start();
            Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
            var closer = new Thread(() => { closeError = Record.Exception(shared.Dispose); closeDone.Set(); }) { IsBackground = true };
            closer.Start();
            var operations = Field(core, "_operations");
            Assert.True(SpinWait.SpinUntil(() => (int)Field(operations, "_waitingExclusive") != 0, TimeSpan.FromSeconds(5)));
            resume.Set();
            var completed = readerDone.Wait(TimeSpan.FromSeconds(3)) && closeDone.Wait(TimeSpan.FromSeconds(3));
            Assert.True(completed, "Reader callback and disposer deadlocked after core-close drain was established.");
            Assert.NotNull(reentryError);
            Assert.Null(readError);
            Assert.Null(closeError);
            if (snapshot) File.Delete(obstruction);
            using (var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password }))
                Assert.Equal(5, cold.GetCollection("rows").Count());
            file.Dispose();
        }
        [Fact]
        public void Review_owner_exit_during_transferred_unleased_reader_callback_does_not_deadlock()
        {
            var file = new TempFile();
            using (var seed = new LiteDatabase(file))
                seed.GetCollection("rows").Insert(Enumerable.Range(1, 5).Select(i => new BsonDocument { ["_id"] = i }));
            File.WriteAllText(file.Filename + "-readers", "no leases");
            var published = new ManualResetEventSlim();
            var exitOwner = new ManualResetEventSlim();
            var reached = new ManualResetEventSlim();
            var resume = new ManualResetEventSlim();
            var done = new ManualResetEventSlim();
            Action callback = null;
            var shared = new SharedEngine(new EngineSettings { Filename = file, ReadOnly = true,
                ReadTransform = (_, value) => { callback?.Invoke(); return value; } });
            IBsonDataReader reader = null;
            var creator = new Thread(() => { reader = shared.Query("rows", new Query()); reader.Read(); published.Set(); exitOwner.Wait(); }) { IsBackground = true };
            creator.Start();
            Assert.True(published.Wait(TimeSpan.FromSeconds(5)));
            var core = ((System.Collections.Generic.HashSet<LiteEngine>)Field(shared, "_mutexSnapshots")).Single();
            callback = () => { callback = null; reached.Set(); resume.Wait(); Record.Exception(() => shared.Pragma("USER_VERSION")); };
            var advancing = new Thread(() => { try { reader.Read(); } finally { done.Set(); } }) { IsBackground = true };
            advancing.Start();
            Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
            exitOwner.Set();
            Assert.True(creator.Join(TimeSpan.FromSeconds(5)));
            var operations = Field(core, "_operations");
            Assert.True(SpinWait.SpinUntil(() => (int)Field(operations, "_waitingExclusive") != 0, TimeSpan.FromSeconds(5)));
            resume.Set();
            Assert.True(done.Wait(TimeSpan.FromSeconds(3)), "Owner-exit cleanup deadlocked with transferred reader's late callback.");
            reader.Dispose();
            shared.Dispose();
            File.Delete(file.Filename + "-readers");
            file.Dispose();
        }
    }
}
