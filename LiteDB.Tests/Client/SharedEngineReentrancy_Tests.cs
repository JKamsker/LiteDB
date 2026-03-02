using System;
using System.IO;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Tests.Utils;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class SharedEngineReentrancy_Tests
    {
        private sealed class ThrowOnDisposeEngine : LiteEngine
        {
            public ThrowOnDisposeEngine()
                : base(new EngineSettings { DataStream = new MemoryStream() })
            {
            }

            protected override void Dispose(bool disposing)
            {
                throw new InvalidOperationException("Test engine dispose failure.");
            }
        }

        [Fact]
        public void SharedEngine_should_release_mutex_for_nested_operations()
        {
            using var file = new TempFile();

            var shared = new SharedEngine(new EngineSettings
            {
                Filename = file.Filename
            });

            try
            {
                var queryDatabase = typeof(SharedEngine).GetMethod("QueryDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
                queryDatabase.Should().NotBeNull();

                var generic = queryDatabase.MakeGenericMethod(typeof(int));

                Func<int> nested = () =>
                {
                    shared.Pragma(Pragmas.UTC_DATE);
                    return 1;
                };

                generic.Invoke(shared, new object[] { nested });

                var mutexField = typeof(SharedEngine).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
                mutexField.Should().NotBeNull();

                var mutex = (Mutex)mutexField.GetValue(shared);
                mutex.Should().NotBeNull();

                var acquired = false;
                Exception error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        acquired = mutex.WaitOne(0);
                        if (acquired)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                thread.Start();
                thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

                error.Should().BeNull();
                acquired.Should().BeTrue();
            }
            finally
            {
                shared.Dispose();
            }
        }

        [Fact]
        public void SharedEngine_should_not_leak_mutex_depth_on_reentrant_BeginTrans()
        {
            using var file = new TempFile();
            using var shared = new SharedEngine(new EngineSettings { Filename = file.Filename });

            shared.BeginTrans().Should().BeTrue();
            shared.BeginTrans().Should().BeFalse();
            shared.Commit().Should().BeTrue();

            var mutexField = typeof(SharedEngine).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
            mutexField.Should().NotBeNull();

            var mutex = (Mutex)mutexField.GetValue(shared);
            mutex.Should().NotBeNull();

            var acquired = false;
            Exception error = null;

            var thread = new Thread(() =>
            {
                try
                {
                    acquired = mutex.WaitOne(0);
                    if (acquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            thread.Start();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

            error.Should().BeNull();
            acquired.Should().BeTrue();
        }

        [Fact]
        public void SharedEngine_should_release_mutex_even_when_engine_dispose_throws()
        {
            using var file = new TempFile();

            var shared = new SharedEngine(new EngineSettings { Filename = file.Filename });

            try
            {
                var engineField = typeof(SharedEngine).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic);
                engineField.Should().NotBeNull();
                engineField.SetValue(shared, new ThrowOnDisposeEngine());

                var openDatabase = typeof(SharedEngine).GetMethod("OpenDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
                openDatabase.Should().NotBeNull();
                openDatabase.Invoke(shared, null);

                var closeDatabase = typeof(SharedEngine).GetMethod("CloseDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
                closeDatabase.Should().NotBeNull();

                Action act = () => closeDatabase.Invoke(shared, null);

                act.Should().Throw<TargetInvocationException>()
                    .Where(ex => ex.InnerException is InvalidOperationException);

                var mutexField = typeof(SharedEngine).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
                mutexField.Should().NotBeNull();

                var mutex = (Mutex)mutexField.GetValue(shared);
                mutex.Should().NotBeNull();

                var acquired = false;
                Exception error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        acquired = mutex.WaitOne(0);
                        if (acquired)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                thread.Start();
                thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

                error.Should().BeNull();
                acquired.Should().BeTrue();
            }
            finally
            {
                shared.Dispose();
            }
        }

        [Fact]
        public void SharedEngine_Dispose_should_be_idempotent_and_prevent_further_operations()
        {
            using var file = new TempFile();

            var shared = new SharedEngine(new EngineSettings { Filename = file.Filename });

            shared.Dispose();
            shared.Invoking(x => x.Dispose()).Should().NotThrow();

            shared.Invoking(x => x.Pragma(Pragmas.UTC_DATE))
                .Should()
                .Throw<ObjectDisposedException>();
        }

        [Fact]
        public void SharedDataReader_should_not_poison_reader_when_disposed_from_wrong_thread()
        {
            using var file = new TempFile();

            var shared = new SharedEngine(new EngineSettings { Filename = file.Filename });

            try
            {
                shared.Insert("col", new[] { new BsonDocument { ["_id"] = 1 } }, BsonAutoId.Int32);

                var reader = shared.Query("col", Query.All());

                Exception disposeException = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        reader.Dispose();
                    }
                    catch (Exception ex)
                    {
                        disposeException = ex;
                    }
                });

                thread.Start();
                thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

                disposeException.Should().BeOfType<InvalidOperationException>();

                reader.Invoking(x => x.Dispose()).Should().NotThrow();

                var mutexField = typeof(SharedEngine).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
                mutexField.Should().NotBeNull();

                var mutex = (Mutex)mutexField.GetValue(shared);
                mutex.Should().NotBeNull();

                var acquired = false;
                Exception error = null;

                var checkThread = new Thread(() =>
                {
                    try
                    {
                        acquired = mutex.WaitOne(0);
                        if (acquired)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                checkThread.Start();
                checkThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

                error.Should().BeNull();
                acquired.Should().BeTrue();
            }
            finally
            {
                shared.Dispose();
            }
        }

        [Fact]
        public void SharedEngine_Commit_should_throw_when_called_from_wrong_thread()
        {
            using var file = new TempFile();

            var shared = new SharedEngine(new EngineSettings { Filename = file.Filename });

            try
            {
                using var transStarted = new ManualResetEventSlim(false);
                using var allowRollback = new ManualResetEventSlim(false);
                using var done = new ManualResetEventSlim(false);

                Exception beginException = null;
                Exception rollbackException = null;

                var txThread = new Thread(() =>
                {
                    try
                    {
                        shared.BeginTrans().Should().BeTrue();
                        transStarted.Set();

                        allowRollback.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

                        shared.Rollback().Should().BeTrue();
                    }
                    catch (Exception ex)
                    {
                        if (beginException == null)
                        {
                            beginException = ex;
                        }
                        else
                        {
                            rollbackException = ex;
                        }
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                txThread.Start();

                transStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                beginException.Should().BeNull();

                shared.Invoking(x => x.Commit())
                    .Should()
                    .Throw<InvalidOperationException>();

                allowRollback.Set();

                done.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                rollbackException.Should().BeNull();

                var mutexField = typeof(SharedEngine).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
                mutexField.Should().NotBeNull();

                var mutex = (Mutex)mutexField.GetValue(shared);
                mutex.Should().NotBeNull();

                var acquired = false;
                Exception error = null;

                var checkThread = new Thread(() =>
                {
                    try
                    {
                        acquired = mutex.WaitOne(0);
                        if (acquired)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                checkThread.Start();
                checkThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

                error.Should().BeNull();
                acquired.Should().BeTrue();
            }
            finally
            {
                shared.Dispose();
            }
        }

        [Fact]
        public void SharedEngine_Rollback_should_throw_when_called_from_wrong_thread()
        {
            using var file = new TempFile();

            var shared = new SharedEngine(new EngineSettings { Filename = file.Filename });

            try
            {
                using var transStarted = new ManualResetEventSlim(false);
                using var allowRollback = new ManualResetEventSlim(false);
                using var done = new ManualResetEventSlim(false);

                Exception beginException = null;
                Exception rollbackException = null;

                var txThread = new Thread(() =>
                {
                    try
                    {
                        shared.BeginTrans().Should().BeTrue();
                        transStarted.Set();

                        allowRollback.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

                        shared.Rollback().Should().BeTrue();
                    }
                    catch (Exception ex)
                    {
                        if (beginException == null)
                        {
                            beginException = ex;
                        }
                        else
                        {
                            rollbackException = ex;
                        }
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                txThread.Start();

                transStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                beginException.Should().BeNull();

                shared.Invoking(x => x.Rollback())
                    .Should()
                    .Throw<InvalidOperationException>();

                allowRollback.Set();

                done.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                rollbackException.Should().BeNull();

                var mutexField = typeof(SharedEngine).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
                mutexField.Should().NotBeNull();

                var mutex = (Mutex)mutexField.GetValue(shared);
                mutex.Should().NotBeNull();

                var acquired = false;
                Exception error = null;

                var checkThread = new Thread(() =>
                {
                    try
                    {
                        acquired = mutex.WaitOne(0);
                        if (acquired)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                checkThread.Start();
                checkThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

                error.Should().BeNull();
                acquired.Should().BeTrue();
            }
            finally
            {
                shared.Dispose();
            }
        }
    }
}
