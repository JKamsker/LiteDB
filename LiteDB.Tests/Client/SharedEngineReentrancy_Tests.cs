using System;
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
    }
}

