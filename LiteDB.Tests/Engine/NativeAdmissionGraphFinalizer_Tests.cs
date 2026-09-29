using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Client.Shared;
using LiteDB.Engine;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionGraphFinalizer_Tests
    {
        private readonly ITestOutputHelper _output;
        public NativeAdmissionGraphFinalizer_Tests(ITestOutputHelper output) { _output = output; }

        private sealed class Observation : IDisposable
        {
            internal readonly string Filename;
            internal readonly ConcurrentQueue<string> Errors = new ConcurrentQueue<string>();
            internal readonly ConcurrentQueue<string> Opened = new ConcurrentQueue<string>();
            internal readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();
            internal readonly ManualResetEventSlim Finalizing = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Resume = new ManualResetEventSlim();
            internal int Started, Finished, Closed, WritableStarted;
            internal bool PausedWritable;
            internal Observation(string filename) { Filename = filename; }

            internal Action<string> Attach(string path, bool writable)
            {
                Opened.Enqueue(path);
                Events.Enqueue("open writable=" + writable + " " + path);
                Check("stream construction");
                var closed = 0;
                return stage =>
                {
                    try
                    {
                        Events.Enqueue(stage + " writable=" + writable + " " + path);
                        if ((stage == "disposed" || stage == "finalized") &&
                            Interlocked.Exchange(ref closed, 1) == 0) Interlocked.Increment(ref Closed);
                        if (stage == "disposed") return;
                        // Pooled read-only streams can survive the abandoned engine
                        // through ConcurrentBag's thread-local storage. They cannot
                        // flush writes; admission must cover every writable finalizer.
                        if (writable) Check(stage + " writable=" + writable + " " + path);
                        if (stage == "finalizing" && writable) Interlocked.Increment(ref WritableStarted);
                        if (stage == "finalizing" && Interlocked.Increment(ref Started) == 1)
                        {
                            PausedWritable = writable;
                            Finalizing.Set();
                            if (!Resume.Wait(TimeSpan.FromSeconds(10))) Errors.Enqueue("Finalizer controller did not resume");
                        }
                        if (stage == "finalized") Interlocked.Increment(ref Finished);
                    }
                    catch (Exception ex) { Errors.Enqueue("observer: " + ex); }
                };
            }

            private void Check(string stage)
            {
                try { if (!Locked(Filename)) Errors.Enqueue("Native admission missing during " + stage); }
                catch (Exception ex) { Errors.Enqueue(stage + ": " + ex); }
            }

            // Failed assertions can leave more finalizers pending. Their callbacks
            // keep this observation alive; let GC reclaim events after those callbacks.
            public void Dispose() { Resume.Set(); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Abandoned_exhausted_reader_and_engine_retain_native_exclusion_and_committed_state(bool shared, bool youngFirst)
        {
            var file = new TempFile();
            // A failed lifetime assertion can deliberately leave a handle open.
            // Keep that fixture rather than masking the assertion in File.Delete
            // or crashing the test host from TempFile's later finalizer.
            GC.SuppressFinalize(file);
            try
            {
                Exercise(file, shared, youngFirst, _output);
                file.Dispose();
            }
            catch (Exception error)
            {
                RetainedTestFixture.Publish(file.Filename, error, _output);
                throw;
            }
        }

        private static void Exercise(TempFile file, bool shared, bool youngFirst, ITestOutputHelper output)
        {
            using (var seed = new LiteDatabase(file))
            {
                seed.GetCollection("rows").InsertBulk(Enumerable.Range(1, 3000)
                    .Select(i => new BsonDocument { ["_id"] = i, ["value"] = i * 2 }));
                seed.GetCollection("rows").EnsureIndex("value", true);
                seed.GetCollection("untouched").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 99 });
            }
            // macOS system temp commonly resolves through /var -> /private/var;
            // assert the same canonical storage namespace used by engine settings.
            var canonical = DatabaseFileIdentity.CanonicalPath(file);
            using var observation = new Observation(canonical);
            var root = CreateGraph(canonical, shared, observation);
            try
            {
                if (Environment.GetEnvironmentVariable("LITEDB_GRAPH_RETENTION_SENTINEL") == "1")
                    throw new InvalidOperationException("graph retention sentinel after real graph construction");
                observation.Errors.Should().BeEmpty();
                observation.Opened.Should().Contain(canonical);
                observation.Opened.Should().Contain(FileHelper.GetLogFile(canonical));
                Locked(file).Should().BeTrue();
                if (youngFirst)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    AssertPromoted(root);
                }
            }
            finally { root.Free(); }
            if (youngFirst)
            {
                GC.Collect(0, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                Locked(file).Should().BeTrue("the abandoned older engine graph survived a young collection");
                observation.Started.Should().Be(0);
            }
            try
            {
                var observedPause = false;
                var deadline = Stopwatch.StartNew();
                do
                {
                    var collection = Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); });
                    try
                    {
                        if (!observedPause)
                        {
                            WaitHandle.WaitAny(new[] { observation.Finalizing.WaitHandle,
                                ((IAsyncResult)collection).AsyncWaitHandle }, TimeSpan.FromSeconds(10)).Should().NotBe(WaitHandle.WaitTimeout);
                            if (observation.Finalizing.IsSet)
                            {
                                if (observation.PausedWritable)
                                    Locked(file).Should().BeTrue("an actual writable FileStream finalizer is paused before its cleanup");
                                observedPause = true;
                                observation.Resume.Set();
                            }
                        }
                        collection.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                    }
                    catch { observation.Resume.Set(); collection.Wait(TimeSpan.FromSeconds(10)); throw; }
                    if (!Locked(file) && Volatile.Read(ref observation.Closed) == observation.Opened.Count) break;
                    // SharedMutexOwner's holder can root the connection for its
                    // one-second idle lifetime, then subordinate finalizable graphs
                    // need subsequent collections. Read-only pools can also outlive
                    // native admission; require their cleanup within the same bound.
                    Thread.Sleep(100);
                } while (deadline.Elapsed < TimeSpan.FromSeconds(10));
                observation.Started.Should().BeGreaterThan(0, "the real storage finalizer boundary must execute");
                if (!shared) observation.WritableStarted.Should().BeGreaterThan(0, "the abandoned Direct engine owns buffered writers");
                observation.Finished.Should().Be(observation.Started);
                observation.Closed.Should().Be(observation.Opened.Count, "all observed storage streams must eventually close");
                observation.Errors.Should().BeEmpty();
                Locked(file).Should().BeFalse("abandonment eventually releases native admission");
            }
            finally
            {
                observation.Resume.Set();
                output.WriteLine("Finalizers started={0} finished={1}, writable={2}, closed={3}/{4}\n{5}\nErrors:\n{6}",
                    observation.Started, observation.Finished, observation.WritableStarted, observation.Closed, observation.Opened.Count, string.Join("\n", observation.Events),
                    string.Join("\n", observation.Errors));
            }
            for (var i = 0; i < 2; i++)
            {
                using var cold = new LiteDatabase(file);
                var rows = cold.GetCollection("rows");
                rows.Count().Should().Be(3001);
                rows.Find("value = 8000").Single()["_id"].AsInt32.Should().Be(4000);
                ((object)rows.FindById(5000)).Should().BeNull();
                rows.FindById(2)["value"].AsInt32.Should().Be(4);
                cold.GetCollection("untouched").FindById(1)["value"].AsInt32.Should().Be(99);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static GCHandle CreateGraph(string filename, bool shared, Observation observation)
        {
            AppContext.TryGetSwitch(SharedCoordinationPolicy.DisableMappedSwitch, out var before);
            AppContext.SetSwitch(SharedCoordinationPolicy.DisableMappedSwitch, true);
            NativeAdmissionStreamProbe.Attach = observation.Attach;
            try
            {
                ILiteEngine engine = shared ? (ILiteEngine)new SharedEngine(new EngineSettings { Filename = filename })
                    : new LiteEngine(filename);
                var db = new LiteDatabase(engine);
                db.CheckpointSize = 0;
                var rows = db.GetCollection("rows");
                rows.Insert(new BsonDocument { ["_id"] = 4000, ["value"] = 8000 });
                db.BeginTrans();
                rows.Insert(new BsonDocument { ["_id"] = 5000, ["value"] = 10000 });
                rows.Delete(2);
                db.Rollback();
                var reader = engine.Query("rows", Query.All());
                var count = 0;
                while (reader.Read()) count++;
                count.Should().Be(3001);
                if (shared) reader.Should().BeOfType<SharedDataReader>();
                new FileInfo(FileHelper.GetLogFile(filename)).Length.Should().BeGreaterThan(0);
                // Actual connection, engine, cursor, snapshot and stream ownership
                // remain unchanged. Exhaust iteration to release page pins, but do
                // not dispose the reader: its snapshot/stream closure is still owned.
                // Observers never retain any of those objects.
                return GCHandle.Alloc(new object[] { db, engine, reader });
            }
            finally
            {
                NativeAdmissionStreamProbe.Attach = null;
                AppContext.SetSwitch(SharedCoordinationPolicy.DisableMappedSwitch, before);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssertPromoted(GCHandle root)
        {
            foreach (var value in (object[])root.Target) GC.GetGeneration(value).Should().Be(GC.MaxGeneration);
        }

        private static bool Locked(string filename)
        {
            using var probe = new DatabaseFileLock(filename, readOnly: true, create: false);
            return probe.Conflicts(DatabaseFileLock.Admission);
        }
    }
}
